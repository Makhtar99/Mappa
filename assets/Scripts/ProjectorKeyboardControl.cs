using UnityEngine;

// Petit helper de demo : pilote le ProjectorController avec le clavier.
// Ajoute ce composant a n'importe quel GameObject (par exemple le Projector)
// et cablele avec le ProjectorController cible.
//
// Touches par defaut :
//   Espace  : on/off
//   R       : rouge plein
//   G       : vert plein
//   B       : bleu plein
//   W       : blanc pur (canal W)
//   1       : blanc froid (R+V+B)
//   +       : monter le dimmer de 0.1
//   -       : baisser le dimmer de 0.1
public sealed class ProjectorKeyboardControl : MonoBehaviour
{
    public ProjectorController target;

    [Header("Touches")]
    public KeyCode toggleKey  = KeyCode.Space;
    public KeyCode redKey     = KeyCode.R;
    public KeyCode greenKey   = KeyCode.G;
    public KeyCode blueKey    = KeyCode.B;
    public KeyCode whiteWKey  = KeyCode.W;   // canal W dedie
    public KeyCode whiteRgbKey = KeyCode.Alpha1; // R+V+B = blanc froid
    public KeyCode dimUpKey   = KeyCode.Equals;   // '=' ou '+'
    public KeyCode dimDownKey = KeyCode.Minus;

    private void Update()
    {
        if (target == null) return;

        if (Input.GetKeyDown(toggleKey))  target.Toggle();
        if (Input.GetKeyDown(redKey))     target.SetColor(1f, 0f, 0f);
        if (Input.GetKeyDown(greenKey))   target.SetColor(0f, 1f, 0f);
        if (Input.GetKeyDown(blueKey))    target.SetColor(0f, 0f, 1f);
        if (Input.GetKeyDown(whiteRgbKey)) target.SetColor(1f, 1f, 1f);
        if (Input.GetKeyDown(whiteWKey))
        {
            target.SetColor(0f, 0f, 0f); // R+V+B off
            target.SetWhite(1f);          // W plein
        }
        if (Input.GetKeyDown(dimUpKey))   target.SetDimmer(target.dimmer + 0.1f);
        if (Input.GetKeyDown(dimDownKey)) target.SetDimmer(target.dimmer - 0.1f);
    }
}
