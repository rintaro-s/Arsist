using System;
using UnityEngine;

namespace Arsist.Runtime.Data
{
    /// <summary>
    /// PlayerPrefsのラッパー（シンプルな設定保存用）
    /// </summary>
    public static class ArsistPrefs
    {
        // プレフィックスでアプリ固有のキーを識別
        private const string PREFIX = "arsist_";

        #region String

        public static void SetString(string key, string value)
        {
            PlayerPrefs.SetString(PREFIX + key, value);
        }

        public static string GetString(string key, string defaultValue = "")
        {
            return PlayerPrefs.GetString(PREFIX + key, defaultValue);
        }

        #endregion

        #region Int

        public static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(PREFIX + key, value);
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            return PlayerPrefs.GetInt(PREFIX + key, defaultValue);
        }

        #endregion

        #region Float

        public static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(PREFIX + key, value);
        }

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            return PlayerPrefs.GetFloat(PREFIX + key, defaultValue);
        }

        #endregion

        #region Bool

        public static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(PREFIX + key, value ? 1 : 0);
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            return PlayerPrefs.GetInt(PREFIX + key, defaultValue ? 1 : 0) == 1;
        }

        #endregion

        #region DateTime

        public static void SetDateTime(string key, DateTime value)
        {
            PlayerPrefs.SetString(PREFIX + key, value.ToString("o")); // ISO 8601
        }

        public static DateTime GetDateTime(string key, DateTime defaultValue = default)
        {
            var str = PlayerPrefs.GetString(PREFIX + key, "");
            if (string.IsNullOrEmpty(str)) return defaultValue;
            return DateTime.TryParse(str, out var result) ? result : defaultValue;
        }

        #endregion

        #region Vector3

        public static void SetVector3(string key, Vector3 value)
        {
            PlayerPrefs.SetFloat(PREFIX + key + "_x", value.x);
            PlayerPrefs.SetFloat(PREFIX + key + "_y", value.y);
            PlayerPrefs.SetFloat(PREFIX + key + "_z", value.z);
        }

        public static Vector3 GetVector3(string key, Vector3 defaultValue = default)
        {
            if (!HasKey(key + "_x")) return defaultValue;
            return new Vector3(
                PlayerPrefs.GetFloat(PREFIX + key + "_x"),
                PlayerPrefs.GetFloat(PREFIX + key + "_y"),
                PlayerPrefs.GetFloat(PREFIX + key + "_z")
            );
        }

        #endregion

        #region Color

        public static void SetColor(string key, Color value)
        {
            PlayerPrefs.SetFloat(PREFIX + key + "_r", value.r);
            PlayerPrefs.SetFloat(PREFIX + key + "_g", value.g);
            PlayerPrefs.SetFloat(PREFIX + key + "_b", value.b);
            PlayerPrefs.SetFloat(PREFIX + key + "_a", value.a);
        }

        public static Color GetColor(string key, Color defaultValue = default)
        {
            if (!HasKey(key + "_r")) return defaultValue;
            return new Color(
                PlayerPrefs.GetFloat(PREFIX + key + "_r"),
                PlayerPrefs.GetFloat(PREFIX + key + "_g"),
                PlayerPrefs.GetFloat(PREFIX + key + "_b"),
                PlayerPrefs.GetFloat(PREFIX + key + "_a")
            );
        }

        #endregion

        #region Utility

        public static bool HasKey(string key)
        {
            return PlayerPrefs.HasKey(PREFIX + key);
        }

        public static void DeleteKey(string key)
        {
            PlayerPrefs.DeleteKey(PREFIX + key);
        }

        public static void DeleteAll()
        {
            // 注意: これは全PlayerPrefsを削除する
            // PREFIX付きのものだけ削除するには別の方法が必要
            PlayerPrefs.DeleteAll();
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }

        #endregion

        #region Common Settings

        // 音量
        public static float MasterVolume
        {
            get => GetFloat("masterVolume", 1f);
            set => SetFloat("masterVolume", Mathf.Clamp01(value));
        }

        public static float BGMVolume
        {
            get => GetFloat("bgmVolume", 1f);
            set => SetFloat("bgmVolume", Mathf.Clamp01(value));
        }

        public static float SEVolume
        {
            get => GetFloat("seVolume", 1f);
            set => SetFloat("seVolume", Mathf.Clamp01(value));
        }

        // 表示
        public static int QualityLevel
        {
            get => GetInt("qualityLevel", QualitySettings.GetQualityLevel());
            set
            {
                SetInt("qualityLevel", value);
                QualitySettings.SetQualityLevel(value);
            }
        }

        // チュートリアル/初回表示
        public static bool TutorialCompleted
        {
            get => GetBool("tutorialCompleted", false);
            set => SetBool("tutorialCompleted", value);
        }

        public static bool FirstLaunch
        {
            get
            {
                if (!HasKey("firstLaunchDone"))
                {
                    SetBool("firstLaunchDone", true);
                    Save();
                    return true;
                }
                return false;
            }
        }

        // 統計
        public static int TotalLaunches
        {
            get => GetInt("totalLaunches", 0);
            set => SetInt("totalLaunches", value);
        }

        public static DateTime LastPlayDate
        {
            get => GetDateTime("lastPlayDate", DateTime.MinValue);
            set => SetDateTime("lastPlayDate", value);
        }

        #endregion
    }
}
