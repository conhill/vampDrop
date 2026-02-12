using UnityEngine;
using Unity.Entities;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Simple FPS and entity count monitor to identify performance bottlenecks
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour
    {
        private float deltaTime = 0.0f;
        private EntityManager entityManager;
        
        private void Start()
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        }
        
        private void Update()
        {
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
            
            // Log every 3 seconds
            if (Time.frameCount % 180 == 0)
            {
                float fps = 1.0f / deltaTime;
                
                var query = entityManager.CreateEntityQuery(typeof(RiceBallTag));
                int ballCount = query.CalculateEntityCount();
                query.Dispose();
                
                Debug.Log($"[PERFORMANCE] FPS: {fps:F1} | Balls: {ballCount} | Frame Time: {deltaTime * 1000f:F1}ms");
                
                if (fps < 30f)
                {
                    Debug.LogWarning($"[PERFORMANCE] ⚠️ LOW FPS! {fps:F1} fps with {ballCount} balls");
                }
            }
        }
        
        private void OnGUI()
        {
            int w = Screen.width, h = Screen.height;
            
            GUIStyle style = new GUIStyle();
            Rect rect = new Rect(10, 10, w, h * 2 / 100);
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = h * 2 / 50;
            style.normal.textColor = Color.white;
            
            float fps = 1.0f / deltaTime;
            string text = string.Format("{0:0.} FPS ({1:0.0} ms)", fps, deltaTime * 1000.0f);
            
            // Color code by FPS
            if (fps < 20f)
                style.normal.textColor = Color.red;
            else if (fps < 40f)
                style.normal.textColor = Color.yellow;
            else
                style.normal.textColor = Color.green;
            
            GUI.Label(rect, text, style);
        }
    }
}
