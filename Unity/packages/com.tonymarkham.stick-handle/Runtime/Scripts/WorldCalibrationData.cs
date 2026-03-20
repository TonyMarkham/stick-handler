using UnityEngine;

namespace StickHandle.Scripts
{

    [CreateAssetMenu(fileName = "WorldCalibrationData", menuName = "StickHandle/World Calibration Data")]
    public class WorldCalibrationData : ScriptableObject
    {
        public string serverHostAddress = "test-pi:8080";

        // HSV gate — written by HsvFilterController after each scan
        public bool orangeValid; // true when orange bank detects exactly 4 blobs
        public bool greenValid; // true when green bank detects exactly 1 blob
        public bool HsvSatisfied => orangeValid && greenValid;

        // World orientation — written by WorldOrientationController on Save
        public Vector3 cylinderA;
        public Vector3 cylinderB;
        public Vector3 cylinderC;
        public Vector3 cylinderD;
        public float[] transformMatrix; // 3x3 homography (pixel→floor-plane), row-major, 9 elements
        public bool isCalibrated;

        private static string FilePath =>
            System.IO.Path.Combine(Application.persistentDataPath, "world_calibration.json");

        public void SaveToDisk()
        {
            try
            {
                System.IO.File.WriteAllText(FilePath, JsonUtility.ToJson(this, prettyPrint: true));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WorldCalibrationData] Failed to save to disk: {e.Message}");
            }
        }

        public void LoadFromDisk()
        {
            if (!System.IO.File.Exists(FilePath)) return;

            try
            {
                JsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText(FilePath), this);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WorldCalibrationData] Failed to load from disk: {e.Message}");
            }
        }
    }

}
