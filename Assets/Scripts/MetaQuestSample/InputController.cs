using UnityEngine;

/// <summary>
/// Handles basic Hand / Controller input.
/// </summary>
public class InputController : MonoBehaviour
{
    [SerializeField] private GameObject _centerEyeAnchor; // Reference to the center eye anchor in the CameraRig
    [SerializeField] private GameObject _gui; // Reference to the GUI GameObject
    [SerializeField] private RoboflowCaller _roboflowCaller; // Reference to the GUI GameObject

    private void Start()
    {
        _gui.SetActive(false);
    }

    void Update()
    {
        // Open / Close GUI
        if (OVRInput.GetDown(OVRInput.Button.Start))
        {
            _roboflowCaller.onStreamingButtonCLicked();
            _gui.SetActive(_roboflowCaller.IsStreaming);

            if (_gui.activeSelf)
            {
                _gui.transform.position = _centerEyeAnchor.transform.position + _centerEyeAnchor.transform.forward * 0.6f;
                _gui.transform.rotation = Quaternion.LookRotation(_gui.transform.position - _centerEyeAnchor.transform.position);
            }
        }
    }
}