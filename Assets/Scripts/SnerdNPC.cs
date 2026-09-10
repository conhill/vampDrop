using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Handles Snerd NPC interactions for both the FPS scene (step 2) and Base scene (steps 5/6/7).
    /// Set mode in Inspector. Attach to a trigger collider on Snerd's zone.
    /// </summary>
    public class SnerdNPC : MonoBehaviour
    {
        public enum SnerdMode { TutorialFPS, Base }

        [Header("Mode")]
        public SnerdMode mode = SnerdMode.TutorialFPS;

        private bool playerInZone;
        private GUIStyle _guiStyle;

        private TutorialManager Tutorial => TutorialManager.Instance;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player")) playerInZone = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player")) playerInZone = false;
        }

        private void Update()
        {
            if (!playerInZone) return;
            if (Input.GetKeyDown(KeyCode.E)) Interact();
        }

        private void Interact()
        {
            // Remember where the player is standing so any comic / scene reload this interaction
            // triggers returns them here instead of the scene's default spawn.
            if (Tutorial != null)
            {
                var pl = GameObject.FindGameObjectWithTag("Player");
                if (pl != null)
                    Tutorial.SavePlayerPosition(pl.transform.position, pl.transform.rotation);
            }

            if (mode == SnerdMode.TutorialFPS)
            {
                if (Tutorial == null || !Tutorial.tutorialActive) return;
                if (Tutorial.tutorialStep == 2)
                {
                    Tutorial.NotifySnerdTalkedFPS();
                    Destroy(gameObject);
                }
                return;
            }

            // Base mode: open the full shop UI once the tutorial is done OR once the player has
            // completed their first drop (TutorialCompleted), so it's reachable during the final
            // "Earn Skrilla" step instead of being locked behind it.
            bool shopUnlocked = Tutorial == null
                || !Tutorial.tutorialActive
                || (PlayerDataManager.Instance != null && PlayerDataManager.Instance.TutorialCompleted);
            if (shopUnlocked)
            {
                SnerdBaseShopUI.Instance?.Open();
                return;
            }

            switch (Tutorial.tutorialStep)
            {
                case 5:
                    // Snerd hands over the rice crafting device; the player crafts themselves after.
                    if (PlayerDataManager.Instance != null)
                        PlayerDataManager.Instance.HasCraftingDevice = true;
                    Tutorial.NotifySnerdTalkedBase1();
                    break;
                // Step 6 (Craft Riceballs) is now done by the player with the device — not at Snerd.
                case 7:
                    Tutorial.NotifySnerdTalkedBase2();
                    break;
            }
        }

        private string GetPrompt()
        {
            if (mode == SnerdMode.TutorialFPS)
            {
                if (Tutorial == null || !Tutorial.tutorialActive) return "";
                return Tutorial.tutorialStep == 2 ? "Press [E] to talk to Snerd" : "";
            }

            // Base mode — always show shop prompt once tutorial is done
            if (Tutorial == null || !Tutorial.tutorialActive)
                return (Vampire.SnerdBaseShopUI.Instance?.IsOpen == true) ? "" : "Press [E] to open Snerd's Shop";

            switch (Tutorial.tutorialStep)
            {
                case 5: return "Press [E] to get the crafting device from Snerd";
                case 6: return ""; // crafting is done by the player with the device, not at Snerd
                case 7: return "Press [E] to talk to Snerd";
                default: return "";
            }
        }

        private void OnGUI()
        {
            if (!playerInZone) return;

            string prompt = GetPrompt();
            if (string.IsNullOrEmpty(prompt)) return;

            if (_guiStyle == null)
            {
                _guiStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize  = 16,
                    alignment = TextAnchor.MiddleCenter
                };
                _guiStyle.normal.textColor     = Color.white;
                _guiStyle.normal.background    = MakeTexture(2, 2, new Color(0f, 0f, 0f, 0.7f));
            }

            float w = 440, h = 60;
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height - h - 100, w, h), prompt, _guiStyle);
        }

        private static Texture2D MakeTexture(int w, int h, Color color)
        {
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
