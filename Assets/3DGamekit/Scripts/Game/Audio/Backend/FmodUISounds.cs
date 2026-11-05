using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gamekit3D
{
    /// <summary>
    /// FMOD versions of the menu sounds. Put it on a menu root (MenuCanvases); every Button underneath gets:
    ///   Hover  - when the mouse moves over it (original: Menu_Buttons_Mouse_Over)
    ///   Back   - on click, for close (X) and Back buttons (original: Menu_Buttons_Back)
    ///   Select - on click, for every other button (original: Menu_Buttons_Confirm)
    /// The original sounds are PlayOneShot calls on the menu's AudioSource and keep working in Unity mode.
    /// UI events are 2D, and play while the game is paused (FMOD runs on real time).
    /// </summary>
    public class FmodUISounds : MonoBehaviour
    {
        public EventReference hoverEvent;
        public EventReference selectEvent;
        public EventReference backEvent;

        [Tooltip("Buttons whose name or parent's name contains one of these play Back instead of Select.")]
        public string[] backButtonNames = { "Close", "Back" };

        [Tooltip("Hook every Button in this scene, not just the ones under this object (e.g. the Start screen, " +
                 "whose menu is split across several canvases).")]
        public bool wholeScene;

        void Awake()
        {
            var buttons = new List<Button>();
            if (wholeScene)
            {
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    buttons.AddRange(root.GetComponentsInChildren<Button>(true));
            }
            else
            {
                buttons.AddRange(GetComponentsInChildren<Button>(true));
            }

            foreach (var button in buttons)
            {
                bool isBack = IsBackButton(button.transform);
                button.onClick.AddListener(() => Play(isBack ? backEvent : selectEvent));

                var trigger = button.GetComponent<EventTrigger>();
                if (trigger == null)
                    trigger = button.gameObject.AddComponent<EventTrigger>();

                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener(_ =>
                {
                    if (button.IsInteractable())
                        Play(hoverEvent);
                });
                trigger.triggers.Add(entry);
            }
        }

        bool IsBackButton(Transform t)
        {
            // The kit names them "UICloseButton" and ".../BackButtonCanvas/UIButton".
            for (int depth = 0; depth < 2 && t != null; depth++, t = t.parent)
            {
                foreach (var name in backButtonNames)
                {
                    if (t.name.Contains(name))
                        return true;
                }
            }
            return false;
        }

        static void Play(EventReference eventRef)
        {
            FmodAudio.StartAndRelease(FmodAudio.CreateInstance(eventRef));
        }
    }
}
