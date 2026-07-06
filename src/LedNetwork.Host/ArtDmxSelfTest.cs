using System.Buffers.Binary;
using LedNetwork.Core.ArtNet;

namespace LedNetwork.Host;

/// <summary>
/// Petit banc de test manuel pour <see cref="ArtDmxPacket"/>.
/// Se lance avec : <c>dotnet run --project src/LedNetwork.Host -- --test-artdmx</c>
/// (ou depuis Visual Studio en passant l'argument <c>--test-artdmx</c>).
///
/// Ce n'est pas un vrai framework de test : ça vérifie les invariants clés de
/// Serialize()/TryParse() et affiche un résumé. Retourne un code de sortie
/// non nul si un cas échoue (utile en CI plus tard).
/// </summary>
internal static class ArtDmxSelfTest
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        Console.WriteLine("=== Auto-test ArtDmxPacket ===\n");

        TestHeaderAndStructure();
        TestRoundTrip();
        TestOddLengthPadding();
        TestPortAddressSplit();
        TestRejections();

        Console.WriteLine($"\nRésultat : {_passed} OK, {_failed} échec(s).");
        return _failed == 0 ? 0 : 1;
    }

    // 1) En-tête ASCII, OpCode, version protocole et taille du buffer.
    private static void TestHeaderAndStructure()
    {
        var packet = new ArtDmxPacket { Data = new byte[512] };
        byte[] bytes = packet.Serialize();

        Check("Taille buffer = 18 + 512", bytes.Length == 18 + 512);
        Check("En-tête \"Art-Net\\0\"", bytes[..8].SequenceEqual(ArtNetConstants.Id));
        Check("OpCode 0x5000 (little-endian)",
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)) == ArtNetConstants.OpDmx);
        Check("Version protocole = 0.14",
            bytes[10] == ArtNetConstants.ProtocolVersionHi && bytes[11] == ArtNetConstants.ProtocolVersionLo);
    }

    // 2) Aller-retour Serialize -> TryParse (longueur paire = exact).
    private static void TestRoundTrip()
    {
        var original = new ArtDmxPacket
        {
            Sequence = 42,
            Physical = 3,
            PortAddress = 0x0A05, // Net = 0x0A, SubUni = 0x05
            Data = new byte[] { 10, 20, 30, 40, 255, 0 },
        };

        byte[] bytes = original.Serialize();
        bool ok = ArtDmxPacket.TryParse(bytes, out var parsed);

        Check("TryParse réussit", ok && parsed is not null);
        if (parsed is null) return;

        Check("Sequence conservée", parsed.Sequence == original.Sequence);
        Check("Physical conservé", parsed.Physical == original.Physical);
        Check("PortAddress conservée", parsed.PortAddress == original.PortAddress);
        Check("Data identique", parsed.Data.SequenceEqual(original.Data));
    }

    // 3) Longueur DMX impaire -> arrondie au pair supérieur (règle Art-Net).
    private static void TestOddLengthPadding()
    {
        var packet = new ArtDmxPacket { Data = new byte[] { 1, 2, 3 } }; // 3 -> 4
        byte[] bytes = packet.Serialize();

        int lengthField = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(16));
        Check("Longueur impaire (3) -> paddée à 4", lengthField == 4);
        Check("Taille buffer = 18 + 4", bytes.Length == 18 + 4);
        Check("Octet de padding = 0", bytes[18 + 3] == 0);
    }

    // 4) Découpage de la Port-Address 15 bits en Net (haut) et SubUni (bas).
    private static void TestPortAddressSplit()
    {
        var packet = new ArtDmxPacket { PortAddress = 0x7FFF, Data = new byte[] { 1, 2 } };

        Check("Net = 0x7F", packet.Net == 0x7F);
        Check("SubUni = 0xFF", packet.SubUni == 0xFF);

        byte[] bytes = packet.Serialize();
        Check("Octet 14 = SubUni", bytes[14] == packet.SubUni);
        Check("Octet 15 = Net", bytes[15] == packet.Net);
    }

    // 5) TryParse doit rejeter ce qui n'est pas de l'ArtDMX valide.
    private static void TestRejections()
    {
        Check("Rejet : datagramme trop court (<18)",
            !ArtDmxPacket.TryParse(new byte[10], out _));

        var badHeader = new byte[20];
        Check("Rejet : mauvais en-tête",
            !ArtDmxPacket.TryParse(badHeader, out _));

        // En-tête correct mais mauvais OpCode (OpPoll au lieu d'OpDmx).
        var wrongOp = new byte[20];
        ArtNetConstants.Id.CopyTo(wrongOp.AsSpan());
        BinaryPrimitives.WriteUInt16LittleEndian(wrongOp.AsSpan(8), ArtNetConstants.OpPoll);
        Check("Rejet : mauvais OpCode",
            !ArtDmxPacket.TryParse(wrongOp, out _));
    }

    private static void Check(string label, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [OK]   {label}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {label}");
        }
    }
}
