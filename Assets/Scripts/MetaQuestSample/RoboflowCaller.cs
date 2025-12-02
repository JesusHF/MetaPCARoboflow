using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using System.Collections;
using System;
using System.Collections.Generic;
using Meta.XR;

/// <summary>
/// Handles webcam streaming, sending frames to Roboflow, receiving detections, and rendering tracked objects in 3D space.
/// </summary>
public class RoboflowCaller : MonoBehaviour
{
    private PassthroughCameraAccess _cameraAccess;
    private Texture2D _texture2D = null; // Used for sending frames to Roboflow
    private bool _isStreaming = false; // Streaming toggle

    [Header("Camera & Streaming")]
    [SerializeField] private RawImage _imageDisplay; // UI display for webcam feed

    [Header("3D Scene References")]
    [SerializeField] private EnvironmentRaycastManager _envRaycastManager;
    [SerializeField] private GameObject _centerEyeAnchor;
    [SerializeField] private GameObject _streamingFeedbackGUI;

    [Header("Tracked Marker Objects")]
    [SerializeField] private GameObject _markerPrefab; // Prefabs to instantiate
    [SerializeField] private List<string> rfClassNames; // Names of classes for UI
    private Dictionary<int, RoboflowObject> _activeMarkerMap = new(); // runtime pool
    [SerializeField] private float minConfidence = 0.8f; // Detection confidence threshold

    [Header("Roboflow API Configuration")]
    [SerializeField] private string RF_MODEL = ""; // Model name for Roboflow
    [SerializeField] private bool USE_LOCAL_SERVER = false; // Toggle for local server usage
    [SerializeField] private string LOCAL_SERVER_IP_ADDRESS = "http://192.168.0.220:9001"; // Local server URL for Roboflow
    private RoboflowInferenceClient client; // API client

    private Texture2D result; // Texture for resized images
    private const int targetWidth = 512; // Target width for resized images
    private const int targetHeight = 512; // Target height for resized images

    private void Awake()
    {
        Assert.IsNotNull(_imageDisplay, "_imageDisplay is not assigned.");
        Assert.IsNotNull(_envRaycastManager, "_envRaycastManager is not assigned.");
        Assert.IsNotNull(_centerEyeAnchor, "_centerEyeAnchor is not assigned.");
        Assert.IsNotNull(_streamingFeedbackGUI, "_streamingFeedbackGUI is not assigned.");
        Assert.IsNotNull(_markerPrefab, "_markerPrefab is not assigned.");
        Assert.IsTrue(rfClassNames != null && rfClassNames.Count > 0, "rfClassNames is not assigned or empty.");
    }

    private void Start()
    {
        // Initialize Roboflow client with local server URL
        if (USE_LOCAL_SERVER)
        {
            client = new RoboflowInferenceClient(APIKeys.RF_API_KEY, LOCAL_SERVER_IP_ADDRESS);
        }
        else
        {
            client = new RoboflowInferenceClient(APIKeys.RF_API_KEY, "https://serverless.roboflow.com", RoboflowInferenceClient.ApiMode.Hosted, RoboflowInferenceClient.HostedModelType.ObjectDetection);
        }
        BuildObjectPool();

        _streamingFeedbackGUI.SetActive(false);
        result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        setupCamera();
    }

    private void Update()
    {
        // Open / Close Feeback
        if (OVRInput.GetDown(OVRInput.Button.Start))
        {
            onStreamingButtonCLicked();
            _streamingFeedbackGUI.SetActive(_isStreaming);
            if (_isStreaming)
            {
                _streamingFeedbackGUI.transform.position = _centerEyeAnchor.transform.position + _centerEyeAnchor.transform.forward * 0.6f;
                _streamingFeedbackGUI.transform.rotation = Quaternion.LookRotation(_streamingFeedbackGUI.transform.position - _centerEyeAnchor.transform.position);
            }
        }
    }

    /// <summary>
    /// Sets up the object pool for Roboflow objects.
    /// </summary>
    private void BuildObjectPool()
    {
        // Build marker pool dynamically
        for (var i = 0; i < rfClassNames.Count; i++)
        {
            var instance = Instantiate(_markerPrefab, Vector3.zero, Quaternion.identity);
            var rfObject = instance.GetComponent<RoboflowObject>();
            rfObject.Init(rfClassNames[i], i); // Initialize with class name and ID
            _activeMarkerMap[rfObject.ClassID] = rfObject;
        }
    }

    /// <summary>
    /// Starts/stops the streaming coroutine.
    /// </summary>
    public void onStreamingButtonCLicked()
    {
        if (_isStreaming)
        {
            _isStreaming = false;
            StopAllCoroutines();
            clearPreviousMarkers();
            Debug.Log("Streaming stopped.");
        }
        else
        {
            _isStreaming = true;
            StartCoroutine(callRoboflow());
            Debug.Log("Streaming started.");
        }
    }

    /// <summary>
    /// Initializes the webcam texture and sets it to the image display.
    /// </summary>
    private void setupCamera()
    {
        Debug.Log("Setup Camera...");

        _cameraAccess = gameObject.AddComponent<PassthroughCameraAccess>();
        _cameraAccess.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
        _cameraAccess.RequestedResolution = new Vector2Int(1280, 960);

        if (_cameraAccess.enabled)
        {
            _texture2D = _cameraAccess.GetTexture() as Texture2D;
            if (_imageDisplay != null)
            {
                _imageDisplay.texture = _texture2D;
            }
        }
    }

    /// <summary>
    /// Scales down a texture to the given dimensions.
    /// </summary>
    private Texture2D resizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        var rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        rt.filterMode = FilterMode.Bilinear;
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        Graphics.Blit(source, rt);
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private IEnumerator callRoboflow()
    {
        while (true)
        {
            if (_texture2D == null)
            {
                yield return null;
            }

            if (_cameraAccess != null && _cameraAccess.enabled)
            {
                _texture2D = _cameraAccess.GetTexture() as Texture2D;
            }

            byte[] jpg = resizeTexture(_texture2D, 512, 512).EncodeToJPG(80);
            string base64Image = Convert.ToBase64String(jpg);
            var image = new InferenceRequestImage("base64", base64Image);

            bool isDone = false;
            // Call Roboflow and wait for completion
            yield return StartCoroutine(client.InferObjectDetection(
                new ObjectDetectionInferenceRequest(RF_MODEL, image),
                response => { OnResponse(response); isDone = true; },
                error => { Debug.Log(error); isDone = true; }
            ));

            yield return new WaitUntil(() => isDone);
        }
    }

    /// <summary>
    /// Callback for successful inference response.
    /// </summary>
    private void OnResponse(ObjectDetectionInferenceResponse response)
    {
        if (response.Predictions != null && response.Predictions.Count > 0)
        {
            foreach (var pred in response.Predictions)
                Debug.Log($"Detected {pred.Class} at ({pred.X},{pred.Y}) confidence: {pred.Confidence}");
            renderDetections(response.Predictions);
        }
        else
        {
            Debug.Log("No predictions found.");
        }
    }

    /// <summary>
    /// Clears all previously tracked markers.
    /// </summary>
    private void clearPreviousMarkers()
    {
        foreach (var marker in _activeMarkerMap.Values)
        {
            if (marker == null) continue;
            marker.Disable();
        }
    }

    /// <summary>
    /// Returns an existing marker for a given class ID.
    /// </summary>
    private RoboflowObject checkForExistingMarker(int classID)
    {
        _activeMarkerMap.TryGetValue(classID, out var marker);
        return marker;
    }

    /// <summary>
    /// Projects 2D detections into 3D space using raycasting and renders marker objects. Copy from Rob's PCA samples.
    /// </summary>
    public void renderDetections(List<ObjectDetectionPrediction> predictions)
    {
        Vector2Int camRes = _cameraAccess.CurrentResolution;
        float halfWidth = targetWidth * 0.5f;
        float halfHeight = targetHeight * 0.5f;

        for (int i = 0; i < predictions.Count; i++)
        {
            ObjectDetectionPrediction prediction = predictions[i];

            if (prediction.Confidence < minConfidence)
            {
                Debug.Log($"Detection {i} below threshold.");
                continue;
            }

            RoboflowObject marker = checkForExistingMarker(prediction.Class_Id);
            if (marker == null)
            {
                Debug.Log($"No marker assigned for class {prediction.Class_Id}");
                continue;
            }

            // Convert center to pixel space
            float adjustedCenterX = prediction.X - halfWidth;
            float adjustedCenterY = prediction.Y - halfHeight;
            float perX = (adjustedCenterX + halfWidth) / targetWidth;
            float perY = (adjustedCenterY + halfHeight) / targetHeight;

            Ray centerRay = _cameraAccess.ViewportPointToRay(new Vector2(perX, 1.0f - perY));
            if (!_envRaycastManager.Raycast(centerRay, out var centerHit))
            {
                Debug.LogWarning("Raycast failed.");
                continue;
            }

            Vector3 markerWorldPos = centerHit.point;
            marker.SuccesfullyTracked(markerWorldPos, _centerEyeAnchor.transform.position);
            marker.SetDebugText(prediction.Class + " " + prediction.Confidence.ToString("F2"));
            Debug.Log($"Placed marker {i} at {markerWorldPos}");
        }
    }
}