using UnityEngine;

namespace UnityLibrary.EditorTools
{
    [ExecuteAlways]
    public class GameViewGridOverlay : MonoBehaviour
    {
#if UNITY_EDITOR
        public bool drawGrid = true;

        [Header("Grid Cell Size (visible area)")]
        public int gridSizeX = 64;
        public int gridSizeY = 64;

        [Header("Spacing Between Cells (invisible gap)")]
        public int spacingX = 16;
        public int spacingY = 16;

        [Header("Start Offsets")]
        public int startOffsetX = 0;
        public int startOffsetY = 0;

        public Color gridColor = new Color(1f, 1f, 1f, 0.5f);

        private void OnGUI()
        {
            if (!drawGrid || Application.isPlaying) return;

            Color oldColor = GUI.color;
            GUI.color = gridColor;

            int cellStrideX = gridSizeX + spacingX;
            int cellStrideY = gridSizeY + spacingY;

            // Loop until start of the cell is beyond screen, not end of cell
            for (int y = startOffsetY; y < Screen.height; y += cellStrideY)
            {
                for (int x = startOffsetX; x < Screen.width; x += cellStrideX)
                {
                    // Draw full box even if it goes beyond screen edges

                    // Left
                    GUI.DrawTexture(new Rect(x, y, 1, gridSizeY), Texture2D.whiteTexture);
                    // Right
                    GUI.DrawTexture(new Rect(x + gridSizeX - 1, y, 1, gridSizeY), Texture2D.whiteTexture);
                    // Top
                    GUI.DrawTexture(new Rect(x, y, gridSizeX, 1), Texture2D.whiteTexture);
                    // Bottom
                    GUI.DrawTexture(new Rect(x, y + gridSizeY - 1, gridSizeX, 1), Texture2D.whiteTexture);
                }
            }

            GUI.color = oldColor;
        }
#endif
    }
}
