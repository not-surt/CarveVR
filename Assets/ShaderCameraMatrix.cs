using UnityEngine;

public class ShaderCameraMatrix : MonoBehaviour {
    public void OnPreCull() {
        Shader.SetGlobalMatrix("_Camera2World", gameObject.GetComponent<Camera>().cameraToWorldMatrix);
    }
}
