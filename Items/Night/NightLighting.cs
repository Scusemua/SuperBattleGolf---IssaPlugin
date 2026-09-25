using UnityEngine;
using UnityEngine.Rendering;

namespace IssaPlugin.Items
{
    /// Applies and restores the night grade on the live skybox and directional light.
    /// An item session leaves the sky alone if Moon replaces it. Force-night takes
    /// a new hole's sky back. Ambient is left alone while Freeze or Low Gravity owns it.
    internal static class NightLighting
    {
        private static Material _nightSky;
        private static Material _sourceSky;
        private static bool _applied;
        private static bool _heldByForce;

        private static AmbientMode _savedAmbientMode;
        private static Color _savedAmbientLight;
        private static float _savedAmbientIntensity;
        private static float _savedReflectionIntensity;
        private static bool _ownsAmbient;

        private static Light _sun;
        private static Color _savedSunColor;
        private static float _savedSunIntensity;
        private static bool _ownsSun;

        private static string _skyColorRaw;
        private static string _horizonColorRaw;
        private static string _sunDirectionRaw;
        private static string _ambientLightRaw;
        private static string _sunColorRaw;
        private static Color _skyColor;
        private static Color _horizonColor;
        private static Vector3 _sunDirection;
        private static Color _ambientLight;
        private static Color _sunColor;

        private static Texture2D _blackCloudTex;
        private static Texture2D _nightGradientTex;
        private static bool _loggedMissingShader;

        /// Keeps night on without an item session. Turning it off restores daylight
        /// unless a Night Time item session is still running.
        public static void SetForced(bool forced)
        {
            if (forced)
            {
                _heldByForce = true;
                if (!_applied)
                    Begin();
                else
                    Maintain();
                return;
            }

            if (!_heldByForce)
                return;

            _heldByForce = false;
            if (!NightItem.IsActive)
                End();
        }

        public static void Begin()
        {
            if (_applied)
                return;

            _applied = true;
            _ownsAmbient = false;
            _ownsSun = false;
            _sun = null;
            RefreshGradeCache(force: true);
            EnsureSky();
            if (RenderSettings.skybox == _nightSky)
                ApplyGrade();
            ApplyLights();
        }

        public static void Maintain()
        {
            if (!_applied)
                return;

            RefreshGradeCache(force: false);
            if (_heldByForce && RenderSettings.skybox != _nightSky)
                EnsureSky();
            if (RenderSettings.skybox == _nightSky)
                ApplyGrade();
            ApplyLights();
        }

        public static void End()
        {
            if (_heldByForce)
                return;

            if (!_applied && _nightSky == null)
                return;

            bool ownSky = _nightSky != null && RenderSettings.skybox == _nightSky;
            if (ownSky)
                RenderSettings.skybox = _sourceSky;

            RestoreLights();

            if (_nightSky != null)
            {
                Object.Destroy(_nightSky);
                _nightSky = null;
            }

            _sourceSky = null;
            _sun = null;
            if (_blackCloudTex != null)
            {
                Object.Destroy(_blackCloudTex);
                _blackCloudTex = null;
            }
            if (_nightGradientTex != null)
            {
                Object.Destroy(_nightGradientTex);
                _nightGradientTex = null;
            }
            _applied = false;
            _ownsAmbient = false;
            _ownsSun = false;
            _loggedMissingShader = false;
        }

        private static void EnsureSky()
        {
            var sky = RenderSettings.skybox;
            if (sky == null || sky == _nightSky)
                return;

            // A new hole may have reset the sun and ambient. Keep the saved
            // daylight only while those values are still the night grade we wrote.
            NoteSceneReset();
            _sourceSky = sky;
            if (_nightSky != null)
                Object.Destroy(_nightSky);
            _nightSky = Object.Instantiate(sky);
            RenderSettings.skybox = _nightSky;
            // The daytime sky gradient and cloud noise stay bright if only the
            // color properties change. Replace them so the sky actually goes dark.
            DarkenSkyTextures(_nightSky);
            DynamicGI.UpdateEnvironment();
        }

        private static void NoteSceneReset()
        {
            var cfg = ModConfig.Night;
            if (
                _ownsAmbient
                && (
                    RenderSettings.ambientMode != AmbientMode.Flat
                    || !ColorsClose(RenderSettings.ambientLight, _ambientLight)
                    || !Mathf.Approximately(RenderSettings.ambientIntensity, cfg.AmbientIntensity.Value)
                )
            )
                _ownsAmbient = false;

            if (
                _ownsSun
                && _sun != null
                && (
                    !ColorsClose(_sun.color, _sunColor)
                    || !Mathf.Approximately(_sun.intensity, cfg.SunIntensity.Value)
                )
            )
            {
                _ownsSun = false;
                _sun = null;
            }
        }

        private static bool ColorsClose(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f
                && Mathf.Abs(a.g - b.g) < 0.02f
                && Mathf.Abs(a.b - b.b) < 0.02f;
        }

        private static void ApplyGrade()
        {
            if (_nightSky == null)
                return;

            var cfg = ModConfig.Night;
            SetColor(_nightSky, "_SkyColor", _skyColor);
            SetColor(_nightSky, "_HorizonColor", _horizonColor);
            SetVector(
                _nightSky,
                "_SunDirection",
                new Vector4(_sunDirection.x, _sunDirection.y, _sunDirection.z, 0f)
            );
            SetFloat(_nightSky, "_MoonCycle", cfg.MoonCycle.Value);
            SetFloat(_nightSky, "_StarsExposure", cfg.StarsExposure.Value);
            SetFloat(_nightSky, "_AmbientIntensity", cfg.SkyAmbient.Value);
        }

        private static void DarkenSkyTextures(Material sky)
        {
            if (_blackCloudTex == null)
            {
                _blackCloudTex = new Texture2D(1, 1, TextureFormat.RGB24, false);
                _blackCloudTex.SetPixel(0, 0, Color.black);
                _blackCloudTex.Apply();
            }

            if (_nightGradientTex == null)
            {
                _nightGradientTex = new Texture2D(1, 64, TextureFormat.RGB24, false);
                _nightGradientTex.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color[64];
                for (int i = 0; i < 64; i++)
                {
                    float t = i / 63f;
                    pixels[i] = Color.Lerp(new Color(0f, 0.01f, 0.05f), new Color(0f, 0f, 0.01f), t);
                }
                _nightGradientTex.SetPixels(pixels);
                _nightGradientTex.Apply();
            }

            SetTexture(sky, "_PerlinTex", _blackCloudTex);
            SetTexture(sky, "_VoronoiTex", _blackCloudTex);
            SetTexture(sky, "_SkyGradientTex", _nightGradientTex);
        }

        private static void ApplyLights()
        {
            // Moon replaced the sky during an item session. Leave its lights alone.
            if (_nightSky != null && RenderSettings.skybox != _nightSky)
                return;

            var cfg = ModConfig.Night;
            bool othersOwnAmbient = FreezeItem.IsFrozen || LowGravityItem.IsActive;

            if (!othersOwnAmbient)
            {
                if (!_ownsAmbient)
                {
                    _savedAmbientMode = RenderSettings.ambientMode;
                    _savedAmbientLight = RenderSettings.ambientLight;
                    _savedAmbientIntensity = RenderSettings.ambientIntensity;
                    _savedReflectionIntensity = RenderSettings.reflectionIntensity;
                    _ownsAmbient = true;
                }

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = _ambientLight;
                RenderSettings.ambientIntensity = cfg.AmbientIntensity.Value;
                RenderSettings.reflectionIntensity = cfg.ReflectionIntensity.Value;
            }

            if (_sun == null)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                {
                    foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    {
                        if (light.type == LightType.Directional && light.isActiveAndEnabled)
                        {
                            _sun = light;
                            break;
                        }
                    }
                }
            }

            if (_sun == null)
                return;

            if (!_ownsSun)
            {
                _savedSunColor = _sun.color;
                _savedSunIntensity = _sun.intensity;
                _ownsSun = true;
            }

            _sun.color = _sunColor;
            _sun.intensity = cfg.SunIntensity.Value;
        }

        private static void RestoreLights()
        {
            if (_ownsAmbient)
            {
                RenderSettings.ambientMode = _savedAmbientMode;
                RenderSettings.ambientLight = _savedAmbientLight;
                RenderSettings.ambientIntensity = _savedAmbientIntensity;
                RenderSettings.reflectionIntensity = _savedReflectionIntensity;
            }

            if (_ownsSun && _sun != null)
            {
                _sun.color = _savedSunColor;
                _sun.intensity = _savedSunIntensity;
            }

            if (_ownsAmbient || _ownsSun)
                DynamicGI.UpdateEnvironment();
        }

        private static void RefreshGradeCache(bool force)
        {
            var cfg = ModConfig.Night;
            if (force || _skyColorRaw != cfg.SkyColor.Value)
            {
                _skyColorRaw = cfg.SkyColor.Value;
                _skyColor = cfg.SkyColorValue;
            }
            if (force || _horizonColorRaw != cfg.HorizonColor.Value)
            {
                _horizonColorRaw = cfg.HorizonColor.Value;
                _horizonColor = cfg.HorizonColorValue;
            }
            if (force || _sunDirectionRaw != cfg.SunDirection.Value)
            {
                _sunDirectionRaw = cfg.SunDirection.Value;
                _sunDirection = cfg.SunDirectionValue;
            }
            if (force || _ambientLightRaw != cfg.AmbientLight.Value)
            {
                _ambientLightRaw = cfg.AmbientLight.Value;
                _ambientLight = cfg.AmbientLightValue;
            }
            if (force || _sunColorRaw != cfg.SunColor.Value)
            {
                _sunColorRaw = cfg.SunColor.Value;
                _sunColor = cfg.SunColorValue;
            }
        }

        private static void SetColor(Material mat, string name, Color value)
        {
            if (mat.HasProperty(name))
                mat.SetColor(name, value);
            else
                LogMissing(mat, name);
        }

        private static void SetVector(Material mat, string name, Vector4 value)
        {
            if (mat.HasProperty(name))
                mat.SetVector(name, value);
            else
                LogMissing(mat, name);
        }

        private static void SetTexture(Material mat, string name, Texture value)
        {
            if (mat.HasProperty(name))
                mat.SetTexture(name, value);
            else
                LogMissing(mat, name);
        }

        private static void SetFloat(Material mat, string name, float value)
        {
            if (mat.HasProperty(name))
                mat.SetFloat(name, value);
            else
                LogMissing(mat, name);
        }

        private static void LogMissing(Material mat, string property)
        {
            if (_loggedMissingShader)
                return;
            _loggedMissingShader = true;
            IssaPluginPlugin.Log.LogWarning(
                $"[Night] Skybox shader '{mat.shader?.name}' is missing '{property}'."
            );
        }
    }
}
