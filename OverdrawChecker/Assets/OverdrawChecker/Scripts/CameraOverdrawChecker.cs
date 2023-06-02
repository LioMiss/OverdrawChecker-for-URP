using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CameraOverdrawChecker : MonoBehaviour
{
    const int GroupDimension = 32;
    const int DataDimension = 128;
    const int DataSize = DataDimension * DataDimension;

    public Camera targetCamera => _targetCamera;

    public long fragmentsCount => isActiveAndEnabled ? _fragmentsCount : 0L;
    public float overdrawRatio => isActiveAndEnabled ? _overdrawRatio : 0f;

    Camera _targetCamera;
    UniversalAdditionalCameraData _targetCameraData;
    RenderTexture _overdrawTexture;
    ComputeShader _computeShader;
    ComputeBuffer _resultBuffer;
    Shader _replacementShader;
    CameraRenderType originalRenderType;
    int[] _inputData;
    int[] _resultData;
    long _fragmentsCount;
    float _overdrawRatio;

    void Awake()
    {
        // Compute shader
        _computeShader = Resources.Load<ComputeShader>("OverdrawParallelReduction");

        // Replacement shader
        _replacementShader = Shader.Find("Debug/OverdrawInt");
        Shader.SetGlobalFloat("OverdrawFragmentWeight", 1f / (GroupDimension * GroupDimension));

        _inputData = new int[DataSize];
        for (int i = 0; i < _inputData.Length; i++)
            _inputData[i] = 0;

        _resultData = new int[DataSize];
        _resultBuffer = new ComputeBuffer(_resultData.Length, 4);
    }

    void OnDestroy()
    {
        ReleaseTexture();
        _resultBuffer?.Release();
    }

    public void SetTargetCamera(Camera targetCamera)
    {
        _targetCamera = targetCamera;
        _targetCameraData = _targetCamera.GetComponent<UniversalAdditionalCameraData>();
        originalRenderType = _targetCameraData.renderType;
    }

    void ReleaseTexture()
    {
        if (_overdrawTexture != null)
        {
            _overdrawTexture.Release();
            _overdrawTexture = null;
        }
    }

    void LateUpdate()
    {
        if (_targetCamera == null)
            return;

        // Save original params
        CameraClearFlags originalClearFlags = _targetCamera.clearFlags;
        Color originalClearColor = _targetCamera.backgroundColor;
        RenderTexture originalTargetTexture = _targetCamera.targetTexture;
        bool originalIsCameraEnabled = _targetCamera.enabled;

        // Recreate texture if needed
        if (_overdrawTexture == null || _targetCamera.pixelWidth != _overdrawTexture.width || _targetCamera.pixelHeight != _overdrawTexture.height)
        {
            ReleaseTexture();
            _overdrawTexture = new RenderTexture(_targetCamera.pixelWidth, _targetCamera.pixelHeight, 24, RenderTextureFormat.RFloat);
        }

        // Set replacement params
        _targetCamera.clearFlags = CameraClearFlags.SolidColor;
        _targetCamera.backgroundColor = Color.clear;
        _targetCamera.targetTexture = _overdrawTexture;
        _targetCamera.enabled = false;
        Camera[] tempCameraStack = null;      
        if (originalRenderType == CameraRenderType.Base && _targetCameraData.cameraStack != null)
        {
            tempCameraStack = _targetCameraData.cameraStack.ToArray();
            _targetCameraData.cameraStack.Clear();
        }
        if (_targetCameraData)
        {
            _targetCameraData.renderType = CameraRenderType.Base;
            _targetCameraData.SetRenderer(1);
        }

        // Render
        _targetCamera.RenderWithShader(_replacementShader, null);

        // Compute
        _resultBuffer.SetData(_inputData);
        int kernel = _computeShader.FindKernel("CSMain");
        _computeShader.SetInt("BufferSizeX", DataDimension);
        _computeShader.SetTexture(kernel, "Overdraw", _overdrawTexture);
        _computeShader.SetBuffer(kernel, "Output", _resultBuffer);

        // Summing up the fragments
        int xGroups = _overdrawTexture.width / GroupDimension;
        int yGroups = _overdrawTexture.height / GroupDimension;
        _computeShader.Dispatch(kernel, xGroups, yGroups, 1);
        _resultBuffer.GetData(_resultData);

        // Results
        _fragmentsCount = 0;
        foreach (int res in _resultData)
            _fragmentsCount += res;
        _overdrawRatio = fragmentsCount / ((float)_overdrawTexture.width * _overdrawTexture.height);

        // Restore original params
        _targetCamera.targetTexture = originalTargetTexture;
        _targetCamera.clearFlags = originalClearFlags;
        _targetCamera.backgroundColor = originalClearColor;
        _targetCamera.enabled = originalIsCameraEnabled;

        if (_targetCameraData)
        {
            _targetCameraData.renderType = originalRenderType;
            _targetCameraData.SetRenderer(0);
            if (tempCameraStack != null)
            {
                foreach (var tempCamera in tempCameraStack)
                {
                    _targetCameraData.cameraStack.Add(tempCamera);
                }
            }
        }
    }
}
