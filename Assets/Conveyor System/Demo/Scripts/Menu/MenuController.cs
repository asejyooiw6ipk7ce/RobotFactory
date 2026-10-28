using System;
using UnityEngine;
using UnityEngine.UI;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Main menu: one card per demo scene. Click a card or press its number key to open the scene; Esc quits.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Menu Controller")]
    public class MenuController : MonoBehaviour
    {
        /// <summary>
        /// One selectable scene.
        /// </summary>
        [Serializable]
        public class Entry
        {
            public string SceneName;

            [Tooltip("Asset path, used in the Editor when the scene is not in the Build Settings.")]
            public string ScenePath;

            [Tooltip("Clickable area of the card.")]
            public RectTransform Card;

            [Tooltip("Outline / frame that lights up on hover.")]
            public Image Frame;
        }

        public Entry[] Entries;

        public Color FrameNormal = new Color(1f, 1f, 1f, 0.08f);
        public Color FrameHover = new Color(1f, 0.55f, 0.15f, 1f);

        [Tooltip("Card scale while hovered.")]
        public float HoverScale = 1.04f;

        private int hovered = -1;

        private void Update()
        {
            Vector2 pointer = DemoInput.PointerPosition;

            int over = -1;
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i].Card != null && RectTransformUtility.RectangleContainsScreenPoint(Entries[i].Card, pointer, null))
                    over = i;

            if (over != hovered)
            {
                hovered = over;
                for (int i = 0; i < Entries.Length; i++)
                {
                    bool on = i == hovered;
                    if (Entries[i].Frame != null) Entries[i].Frame.color = on ? FrameHover : FrameNormal;
                    if (Entries[i].Card != null) Entries[i].Card.localScale = Vector3.one * (on ? HoverScale : 1f);
                }
            }

            if (DemoInput.LeftPressed && hovered >= 0) Open(hovered);

            for (int i = 0; i < Entries.Length && i < 9; i++)
                if (DemoInput.GetKeyDown((DemoKey)(i + 1))) Open(i);

            if (DemoInput.GetKeyDown(DemoKey.Escape)) SceneLoader.Quit();
        }

        /// <summary>
        /// Opens the scene of an entry.
        /// </summary>
        public void Open(int index)
        {
            if (index < 0 || index >= Entries.Length) return;
            SceneLoader.Load(Entries[index].SceneName, Entries[index].ScenePath);
        }
    }
}
