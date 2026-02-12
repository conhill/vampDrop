using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Creates a sphere mesh and material for ball rendering
    /// Add this to the same GameObject as RiceBallRendererECS
    /// </summary>
    public class BallMeshSetup : MonoBehaviour
    {
        private void Awake()
        {
            // Get the renderer component
            var renderer = GetComponent<RiceBallRendererECS>();
            if (renderer == null)
            {
                Debug.LogError("[BallMeshSetup] No RiceBallRendererECS found on this GameObject!");
                return;
            }
            
            // Create sphere mesh if needed
            if (renderer.BallMesh == null)
            {
                // Create CYLINDER mesh for flat "disc" shape - better performance for 2D-style physics
                GameObject tempCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Mesh cylinderMesh = tempCylinder.GetComponent<MeshFilter>().sharedMesh;
                renderer.BallMesh = cylinderMesh;
                Destroy(tempCylinder); // Clean up temp
                
                Debug.Log("[BallMeshSetup] ✅ Created flat cylinder (disc) mesh");
            }
            
            // Create material if needed
            if (renderer.BallMaterial == null)
            {
                Material ballMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (ballMat.shader.name.Contains("Hidden"))
                {
                    // URP not available, use Standard
                    ballMat = new Material(Shader.Find("Standard"));
                }
                
                ballMat.color = Color.white;
                ballMat.enableInstancing = true; // CRITICAL for GPU instancing!
                
                renderer.BallMaterial = ballMat;
                
                Debug.Log($"[BallMeshSetup] ✅ Created ball material (Shader: {ballMat.shader.name}) with GPU instancing");
            }
            
            Debug.Log("[BallMeshSetup] Ball mesh and material ready!");
        }
    }
}
