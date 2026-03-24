// EAC - HallOfHistoryWindow.Portraits
// Extracted portrait loading and portrait cache helpers for Hall of History.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using KSP;

namespace RosterRotation
{
    public partial class HallOfHistoryWindow : MonoBehaviour
    {
        private void DrawPortraitBlock(string name, string role, float w, float h, bool framed, string badgeText = null)
        {
            GUILayout.BeginVertical(framed ? _plaqueBodyStyle : KspGuiSkin.Box, GUILayout.Width(w), GUILayout.Height(h));
            GUILayout.Space(2f);

            Rect rect = GUILayoutUtility.GetRect(w - 12f, h * 0.74f, GUILayout.Width(w - 12f), GUILayout.Height(h * 0.74f));
            Texture tex = TryGetPortraitTexture(name);

            GUI.Box(rect, GUIContent.none);
            if (tex != null)
            {
                DrawPortraitTexture(rect, tex);
            }
            else
            {
                DrawFallbackPortrait(rect, name, role);
            }

            if (!string.IsNullOrEmpty(badgeText))
                DrawPortraitBanner(rect, badgeText);

            GUILayout.Space(4f);
            GUILayout.Label(string.IsNullOrEmpty(role) ? "KERBAL" : role.ToUpperInvariant(), _centerMutedStyle, GUILayout.Width(w - 12f));
            GUILayout.EndVertical();
        }

        private static void DrawPortraitTexture(Rect rect, Texture tex)
        {
            if (tex == null)
                return;

            Material portraitMaterial = PortraitRenderMaterial;
            if (tex is RenderTexture && portraitMaterial != null)
            {
                Graphics.DrawTexture(rect, tex, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0, portraitMaterial);
                return;
            }

            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
        }

        private void DrawPortraitBanner(Rect portraitRect, string badgeText)
        {
            if (string.IsNullOrEmpty(badgeText))
                return;

            Rect badgeRect = new Rect(portraitRect.x + 4f, portraitRect.y + 4f, Mathf.Min(portraitRect.width - 8f, 72f), 20f);
            GUI.Label(badgeRect, badgeText, _pillStyle);
        }

        private void DrawFallbackPortrait(Rect rect, string name, string role)
        {
            GUI.Box(rect, GUIContent.none);

            Texture fallback = GetFallbackPortraitTexture(name);
            if (fallback != null)
            {
                GUI.DrawTexture(rect, fallback, ScaleMode.ScaleToFit, true);
                return;
            }

            string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            Rect initialRect = new Rect(rect.x, rect.y + rect.height * 0.10f, rect.width, rect.height * 0.55f);
            GUI.Label(initialRect, initial, new GUIStyle(KspGuiSkin.Label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(rect.height * 0.42f),
                fontStyle = FontStyle.Bold
            });

            Rect roleRect = new Rect(rect.x + 4f, rect.yMax - 24f, rect.width - 8f, 20f);
            GUI.Label(roleRect, string.IsNullOrEmpty(role) ? "Crew" : role, _centerMutedStyle);
        }

        private Texture GetFallbackPortraitTexture(string kerbalName)
        {
            bool isFemale = IsFemaleKerbal(kerbalName);
            if (isFemale)
            {
                if (_fallbackFemalePortrait == null)
                    _fallbackFemalePortrait = LoadFirstExistingTexture(GetFallbackPortraitPaths(true));
                if (_fallbackFemalePortrait != null)
                    return _fallbackFemalePortrait;
            }

            if (_fallbackMalePortrait == null)
                _fallbackMalePortrait = LoadFirstExistingTexture(GetFallbackPortraitPaths(false));

            return _fallbackMalePortrait != null ? (Texture)_fallbackMalePortrait : _fallbackFemalePortrait;
        }

        private bool IsFemaleKerbal(string kerbalName)
        {
            try
            {
                ProtoCrewMember pcm = FindCrewMemberByName(kerbalName);
                if (pcm == null)
                    return false;

                object genderObj = ReadMemberObject(pcm, pcm.GetType(), "gender", "Gender");
                if (genderObj == null)
                    genderObj = ReadMemberObject(pcm, pcm.GetType(), "sex", "Sex");

                if (genderObj == null)
                    return false;

                string value = genderObj.ToString();
                return !string.IsNullOrEmpty(value) &&
                       value.IndexOf("female", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static Texture2D LoadFirstExistingTexture(IEnumerable<string> paths)
        {
            if (paths == null)
                return null;

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                try
                {
                    if (File.Exists(path))
                    {
                        Texture2D tex = LoadTextureFromFile(path);
                        if (tex != null)
                            return tex;
                    }
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:154", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:154", ex); }
            }

            return null;
        }

        private static IEnumerable<string> GetFallbackPortraitPaths(bool female)
        {
            string root = KSPUtil.ApplicationRootPath;
            if (string.IsNullOrEmpty(root))
                yield break;

            if (female)
            {
                yield return Path.Combine(root, "GameData", "EAC", "PluginData", "HallPortraitFallback_Female.png");
                yield return Path.Combine(root, "GameData", "EAC", "PluginData", "female.png");
            }
            else
            {
                yield return Path.Combine(root, "GameData", "EAC", "PluginData", "HallPortraitFallback_Male.png");
                yield return Path.Combine(root, "GameData", "EAC", "PluginData", "male.png");
            }
        }

        private Texture TryGetPortraitTexture(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName))
                return null;

            Texture cached;
            if (_portraitCache.TryGetValue(kerbalName, out cached))
                return cached;

            Texture tex = TryLoadCapturedPortraitTexture(kerbalName);
            _portraitCache[kerbalName] = tex;
            return tex;
        }

        private Texture TryLoadCapturedPortraitTexture(string kerbalName)
        {
            foreach (string path in GetCapturedPortraitPaths(kerbalName))
            {
                Texture2D tex = LoadTextureFromFile(path);
                if (tex != null)
                    return tex;
            }

            return null;
        }

        private IEnumerable<string> GetCapturedPortraitPaths(string kerbalName)
        {
            string root = KSPUtil.ApplicationRootPath;
            string saveFolder = HighLogic.SaveFolder ?? string.Empty;
            string fileSafeName = MakeSafePortraitFileName(kerbalName);
            string[] names = new[]
            {
                fileSafeName + ".png",
                fileSafeName + ".jpg",
                fileSafeName + ".jpeg",
                (kerbalName ?? string.Empty) + ".png",
                (kerbalName ?? string.Empty) + ".jpg",
                (kerbalName ?? string.Empty) + ".jpeg"
            };

            string[] dirs = new[]
            {
                Path.Combine(root, "GameData", "EAC", "PluginData", "HallPortraits"),
                Path.Combine(root, "saves", saveFolder, "EAC", "HallPortraits"),
                Path.Combine(root, "saves", saveFolder, "PluginData", "EAC", "HallPortraits")
            };

            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir))
                    continue;

                foreach (string name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                        yield return Path.Combine(dir, name);
                }
            }
        }

        private static string MakeSafePortraitFileName(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName))
                return "unknown";

            var sb = new StringBuilder(kerbalName.Length);
            foreach (char ch in kerbalName)
            {
                if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '_' || ch == '-')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }

            return sb.ToString().Trim();
        }

        private List<string> BuildPortraitSearchKeys(string kerbalName)
        {
            var keys = new List<string>();
            AddPortraitSearchKey(keys, kerbalName);

            foreach (string key in GetTextureReplacerPortraitKeys(kerbalName))
                AddPortraitSearchKey(keys, key);

            return keys;
        }

        private static void AddPortraitSearchKey(List<string> keys, string value)
        {
            if (keys == null || string.IsNullOrEmpty(value))
                return;

            value = value.Trim();
            if (value.Length == 0)
                return;

            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            keys.Add(value);
        }

        private Texture TryGetLiveKerbalPortrait(string kerbalName)
        {
            try
            {
                ProtoCrewMember pcm = FindCrewMemberByName(kerbalName);
                if (pcm == null)
                    return null;

                return TryGetAvatarTextureFromProto(pcm);
            }
            catch
            {
                return null;
            }
        }

        private ProtoCrewMember FindCrewMemberByName(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName) || HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null)
                return null;

            var roster = HighLogic.CurrentGame.CrewRoster;

            try
            {
                MethodInfo allKerbals = roster.GetType().GetMethod(
                    "AllKerbals",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

                if (allKerbals != null)
                {
                    IEnumerable all = allKerbals.Invoke(roster, null) as IEnumerable;
                    if (all != null)
                    {
                        foreach (object item in all)
                        {
                            ProtoCrewMember pcm = item as ProtoCrewMember;
                            if (pcm != null && string.Equals(pcm.name, kerbalName, StringComparison.OrdinalIgnoreCase))
                                return pcm;
                        }
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:331", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:331", ex); }

            try
            {
                foreach (ProtoCrewMember pcm in roster.Crew)
                {
                    if (pcm != null && string.Equals(pcm.name, kerbalName, StringComparison.OrdinalIgnoreCase))
                        return pcm;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:341", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:341", ex); }

            return null;
        }

        private static Texture TryGetAvatarTextureFromProto(ProtoCrewMember pcm)
        {
            if (pcm == null)
                return null;

            try
            {
                object kerbalRef = ReadMemberObject(pcm, pcm.GetType(), "KerbalRef", "kerbalRef");
                if (kerbalRef == null)
                    return null;

                return ReadMemberTexture(kerbalRef, kerbalRef.GetType(), "avatarTexture", "AvatarTexture");
            }
            catch
            {
                return null;
            }
        }

        public static string GetPrimaryPortraitCachePath(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName))
                return null;

            string safeName = SanitizePortraitFileName(kerbalName);
            if (string.IsNullOrEmpty(safeName))
                return null;

            string saveFolder = HighLogic.SaveFolder ?? string.Empty;
            return Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder, "EAC", "HallPortraits", safeName + ".png");
        }

        public static bool HasCapturedPortraitCache(string kerbalName)
        {
            try
            {
                string path = GetPrimaryPortraitCachePath(kerbalName);
                return !string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 0L;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryCapturePortraitCache(ProtoCrewMember pcm)
        {
            if (pcm == null || string.IsNullOrEmpty(pcm.name))
                return false;

            try
            {
                Texture tex = TryGetVisiblePortraitTexture(pcm);
                return TryCapturePortraitCache(pcm.name, tex);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryCapturePortraitCache(string kerbalName, Texture sourceTexture)
        {
            if (string.IsNullOrEmpty(kerbalName) || sourceTexture == null)
                return false;

            Texture2D readable = null;
            RenderTexture previous = null;
            bool rtActiveChanged = false;
            try
            {
                int width = Mathf.Max(2, sourceTexture.width);
                int height = Mathf.Max(2, sourceTexture.height);

                if (sourceTexture is Texture2D)
                {
                    // Try the direct CPU path first. KSP's portrait Texture2D is not marked
                    // readable, so GetPixels() will throw — in that case fall through to the
                    // RenderTexture blit path which works on any texture regardless of readability.
                    Texture2D src = (Texture2D)sourceTexture;
                    bool directCopyOk = false;
                    try
                    {
                        Color[] pixels = src.GetPixels();
                        if (pixels != null && pixels.Length > 0)
                        {
                            readable = new Texture2D(src.width, src.height, TextureFormat.ARGB32, false);
                            readable.SetPixels(pixels);
                            readable.Apply(false, false);
                            directCopyOk = true;
                        }
                    }
                    catch { /* not readable — fall through to RT blit below */ }

                    if (!directCopyOk)
                    {
                        // Blit the non-readable Texture2D into a temporary RenderTexture,
                        // then ReadPixels from there — works for any GPU texture.
                        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                        Graphics.Blit(src, rt);
                        previous = RenderTexture.active;
                        RenderTexture.active = rt;
                        rtActiveChanged = true;
                        readable = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
                        readable.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0, false);
                        readable.Apply(false, false);
                        RenderTexture.ReleaseTemporary(rt);
                    }
                }
                else
                {
                    RenderTexture rt = sourceTexture as RenderTexture;
                    if (rt == null)
                    {
                        rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                        Graphics.Blit(sourceTexture, rt);
                    }

                    previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    rtActiveChanged = true;
                    readable = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
                    readable.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0, false);
                    readable.Apply(false, false);

                    if (!(sourceTexture is RenderTexture))
                        RenderTexture.ReleaseTemporary(rt);
                }

                if (!LooksLikeUsablePortrait(readable))
                    return false;

                byte[] png = EncodeTextureToPngBytes(readable);
                if (png == null || png.Length == 0)
                    return false;

                string path = GetPrimaryPortraitCachePath(kerbalName);
                if (string.IsNullOrEmpty(path))
                    return false;

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(path, png);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (rtActiveChanged)
                    RenderTexture.active = previous;
                if (readable != null)
                    UnityEngine.Object.Destroy(readable);
            }
        }

        private static Texture TryGetVisiblePortraitTexture(ProtoCrewMember pcm)
        {
            if (pcm == null)
                return null;

            // Path 1: KerbalPortraitGallery.Instance — the authoritative in-flight portrait manager.
            // Walks the gallery's Portraits list, finds the matching KerbalPortrait by name,
            // and reads its RawImage.texture (the live RenderTexture shown on screen).
            try
            {
                Texture galleryTex = TryGetTextureFromPortraitGallery(pcm);
                if (galleryTex != null)
                    return galleryTex;
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.gallery", "Exception in portrait gallery path", ex); }

            // Path 2: pcm.KerbalRef -> Kerbal -> portrait camera (IVA / EVA only).
            // KerbalRef is null for kerbals inside a capsule in normal flight.
            try
            {
                object kerbalRef = ReadMemberObject(pcm, pcm.GetType(), "KerbalRef", "kerbalRef");
                if (kerbalRef != null)
                {
                    object portrait = ReadMemberObject(kerbalRef, kerbalRef.GetType(), "portrait", "Portrait");
                    if (portrait != null)
                    {
                        Texture camTex = TryGetCameraTargetTexture(portrait);
                        if (camTex != null)
                            return camTex;

                        Texture portraitTex = ReadMemberTexture(portrait, portrait.GetType(), "textureRef", "renderTexture")
                                           ?? ReadMemberTexture(portrait, portrait.GetType(), "PortraitTexture", "portraitTexture", "texture", "Texture");
                        if (portraitTex != null)
                            return portraitTex;
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.kerbalref", "Exception in KerbalRef portrait path", ex); }

            // Path 3: avatarTexture (IVA helmet camera — only populated during IVA).
            return TryGetAvatarTextureFromProto(pcm);
        }

        /// <summary>
        /// Walks KerbalPortraitGallery.Instance.Portraits to find the portrait for the given
        /// ProtoCrewMember, then returns the Camera's targetTexture.
        /// Uses reflection so it compiles without a direct reference to KSP internals.
        /// </summary>
        private static Texture TryGetTextureFromPortraitGallery(ProtoCrewMember pcm)
        {
            if (pcm == null || string.IsNullOrEmpty(pcm.name))
                return null;

            try
            {
                return TryGetTextureFromPortraitGalleryImpl(pcm);
            }
            catch (Exception ex)
            {
                // Log rather than swallow so we see exactly what fails.
                RRLog.Verbose("[EAC.PortraitCapture] Gallery: exception: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static Texture TryGetTextureFromPortraitGalleryImpl(ProtoCrewMember pcm)
        {
            // Find KerbalPortraitGallery by type.Name scan (full namespace varies across builds).
            System.Type galleryType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null) continue;
                System.Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                if (types == null) continue;
                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (string.Equals(t.Name, "KerbalPortraitGallery", StringComparison.OrdinalIgnoreCase))
                    { galleryType = t; break; }
                }
                if (galleryType != null) break;
            }
            if (galleryType == null)
            {
                RRLog.Verbose("[EAC.PortraitCapture] Gallery type not found in any assembly");
                return null;
            }
            RRLog.Verbose("[EAC.PortraitCapture] Gallery type=" + galleryType.FullName);

            // Get singleton Instance.
            object gallery = null;
            try
            {
                var p = galleryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null) gallery = p.GetValue(null, null);
            }
            catch { }
            if (gallery == null)
            {
                try
                {
                    var f = galleryType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null) gallery = f.GetValue(null);
                }
                catch { }
            }
            if (gallery == null)
            {
                RRLog.Verbose("[EAC.PortraitCapture] Gallery Instance=null");
                return null;
            }

            // Find the Portraits list (field name confirmed as "Portraits" in KSP 1.12).
            System.Collections.IEnumerable portraits = null;
            foreach (string listName in new[] { "Portraits", "portraits", "_portraits", "activeCrew", "crew" })
            {
                try
                {
                    var f = galleryType.GetField(listName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) portraits = f.GetValue(gallery) as System.Collections.IEnumerable;
                }
                catch { }
                if (portraits == null)
                {
                    try
                    {
                        var p = galleryType.GetProperty(listName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (p != null) portraits = p.GetValue(gallery, null) as System.Collections.IEnumerable;
                    }
                    catch { }
                }
                if (portraits != null) break;
            }
            if (portraits == null)
            {
                RRLog.Verbose("[EAC.PortraitCapture] Gallery Portraits list not found");
                return null;
            }

            // Walk the list, match by kerbal name, read RawImage.texture.
            // KerbalPortrait stores the live portrait as a RawImage component in field "portrait".
            // The RawImage.texture property holds the RenderTexture rendered by KSP's portrait camera.
            foreach (var portrait in portraits)
            {
                if (portrait == null) continue;
                System.Type pt = portrait.GetType();

                // Resolve kerbal name via the "crewMember" property (type Kerbal, has .name).
                string portraitKerbalName = null;
                foreach (string memberName in new[] { "crewMember", "crew", "pcm", "kerbal" })
                {
                    object val = null;
                    try
                    {
                        var f = pt.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null) val = f.GetValue(portrait);
                    }
                    catch { }
                    if (val == null)
                    {
                        try
                        {
                            var p = pt.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (p != null) val = p.GetValue(portrait, null);
                        }
                        catch { }
                    }
                    if (val == null) continue;
                    var pcmVal = val as ProtoCrewMember;
                    if (pcmVal != null) { portraitKerbalName = pcmVal.name; break; }
                    try
                    {
                        var nameProp = val.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (nameProp != null)
                        {
                            portraitKerbalName = nameProp.GetValue(val, null) as string;
                            if (!string.IsNullOrEmpty(portraitKerbalName)) break;
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(portraitKerbalName) ||
                    !string.Equals(portraitKerbalName, pcm.name, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Matched — read RawImage.texture (the live portrait RenderTexture).
                try
                {
                    var rawImageField = pt.GetField("portrait", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (rawImageField != null)
                    {
                        object rawImage = rawImageField.GetValue(portrait);
                        if (rawImage != null)
                        {
                            var texProp = rawImage.GetType().GetProperty("texture",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (texProp != null)
                            {
                                Texture tex = texProp.GetValue(rawImage, null) as Texture;
                                // Only accept a RenderTexture — that means the portrait camera
                                // has rendered a real frame. A Texture2D here is KSP's static
                                // placeholder shown before the camera initialises; rejecting it
                                // lets the retry loop wait for the live frame.
                                RRLog.Verbose("[EAC.PortraitCapture] Matched " + pcm.name
                                    + " tex=" + (tex != null ? tex.GetType().Name + " " + tex.width + "x" + tex.height : "null")
                                    + (tex != null && !(tex is RenderTexture) ? " (placeholder, skipping)" : ""));
                                if (tex is RenderTexture) return tex;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    RRLog.Verbose("[EAC.PortraitCapture] RawImage.texture read failed: " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Given any object that may have a Camera field/property named "cam", "camera", or similar,
        /// returns that Camera's targetTexture, or null.
        /// </summary>
        private static Texture TryGetCameraTargetTexture(object obj)
        {
            if (obj == null) return null;
            System.Type t = obj.GetType();
            const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (string camName in new[] { "cam", "camera", "portraitCamera", "Camera" })
            {
                object camObj = null;
                var f = t.GetField(camName, bf);
                if (f != null) camObj = f.GetValue(obj);
                else
                {
                    var p = t.GetProperty(camName, bf);
                    if (p != null) camObj = p.GetValue(obj, null);
                }

                var cam = camObj as Camera;
                if (cam != null && cam.targetTexture != null)
                    return cam.targetTexture;
            }

            return null;
        }

        private static string SanitizePortraitFileName(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName))
                return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(kerbalName.Length);
            foreach (char c in kerbalName)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }

        private static bool LooksLikeUsablePortrait(Texture2D tex)
        {
            if (tex == null || tex.width < 8 || tex.height < 8)
                return false;

            try
            {
                int stepX = Mathf.Max(1, tex.width / 8);
                int stepY = Mathf.Max(1, tex.height / 8);
                Color first = tex.GetPixel(0, 0);
                float variance = 0f;
                float brightness = 0f;
                int count = 0;
                for (int y = 0; y < tex.height; y += stepY)
                {
                    for (int x = 0; x < tex.width; x += stepX)
                    {
                        Color c = tex.GetPixel(x, y);
                        brightness += (c.r + c.g + c.b) / 3f;
                        variance += Mathf.Abs(c.r - first.r) + Mathf.Abs(c.g - first.g) + Mathf.Abs(c.b - first.b);
                        count++;
                    }
                }

                if (count <= 0)
                    return false;

                brightness /= count;
                return brightness > 0.03f && variance > 0.25f;
            }
            catch
            {
                return true;
            }
        }

        private static byte[] EncodeTextureToPngBytes(Texture2D tex)
        {
            if (tex == null)
                return null;

            try
            {
                MethodInfo mi = typeof(Texture2D).GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (mi != null)
                    return mi.Invoke(tex, null) as byte[];
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:562", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:562", ex); }

            try
            {
                Type imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (imageConversionType != null)
                {
                    MethodInfo mi = imageConversionType.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null);
                    if (mi != null)
                        return mi.Invoke(null, new object[] { tex }) as byte[];
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:574", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:574", ex); }

            return null;
        }

        private IEnumerable<string> GetTextureReplacerPortraitKeys(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName))
                return Enumerable.Empty<string>();

            EnsureTextureReplacerPortraitKeys();

            List<string> keys;
            if (_textureReplacerPortraitKeys.TryGetValue(NormalizePortraitKey(kerbalName), out keys) && keys != null)
                return keys;

            return Enumerable.Empty<string>();
        }

        private void EnsureTextureReplacerPortraitKeys()
        {
            if (_textureReplacerPortraitKeysReady)
                return;

            _textureReplacerPortraitKeysReady = true;
            _textureReplacerPortraitKeys.Clear();

            string gameData = Path.Combine(KSPUtil.ApplicationRootPath, "GameData");
            if (!Directory.Exists(gameData))
                return;

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string file in Directory.GetFiles(gameData, "*.cfg", SearchOption.AllDirectories))
                    files.Add(file);
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:612", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:612", ex); }

            try
            {
                foreach (string file in Directory.GetFiles(gameData, "*.tcfg", SearchOption.AllDirectories))
                    files.Add(file);
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:619", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:619", ex); }

            foreach (string file in files)
            {
                ConfigNode root = null;
                try
                {
                    root = ConfigNode.Load(file);
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:628", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:628", ex); }

                if (root == null)
                    continue;

                foreach (ConfigNode trNode in EnumerateConfigNodesNamed(root, "TextureReplacer"))
                {
                    foreach (ConfigNode customKerbals in trNode.GetNodes("CustomKerbals"))
                    {
                        foreach (ConfigNode.Value value in customKerbals.values)
                        {
                            if (value == null)
                                continue;

                            string kerbalName = (value.name ?? string.Empty).Trim();
                            string raw = (value.value ?? string.Empty).Trim();
                            if (kerbalName.Length == 0 || raw.Length == 0)
                                continue;

                            string[] tokens = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length == 0)
                                continue;

                            RegisterTextureReplacerPortraitKey(kerbalName, tokens[0]);
                        }
                    }
                }
            }
        }

        private static IEnumerable<ConfigNode> EnumerateConfigNodesNamed(ConfigNode root, string nodeName)
        {
            if (root == null || string.IsNullOrEmpty(nodeName))
                yield break;

            if (string.Equals(root.name, nodeName, StringComparison.OrdinalIgnoreCase))
                yield return root;

            foreach (ConfigNode child in root.nodes)
            {
                foreach (ConfigNode nested in EnumerateConfigNodesNamed(child, nodeName))
                    yield return nested;
            }
        }

        private void RegisterTextureReplacerPortraitKey(string kerbalName, string portraitKey)
        {
            string normalizedKerbal = NormalizePortraitKey(kerbalName);
            if (normalizedKerbal.Length == 0 || string.IsNullOrEmpty(portraitKey))
                return;

            List<string> keys;
            if (!_textureReplacerPortraitKeys.TryGetValue(normalizedKerbal, out keys) || keys == null)
            {
                keys = new List<string>();
                _textureReplacerPortraitKeys[normalizedKerbal] = keys;
            }

            AddPortraitSearchKey(keys, portraitKey);
        }

        private Texture TryFindPortraitTextureInDatabase(IEnumerable<string> searchKeys)
        {
            try
            {
                var db = GameDatabase.Instance;
                if (db == null)
                    return null;

                List<string> keys = searchKeys == null
                    ? new List<string>()
                    : searchKeys.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (keys.Count == 0)
                    return null;

                foreach (string key in keys)
                {
                    foreach (string candidate in BuildPortraitTextureCandidates(key))
                    {
                        Texture direct = db.GetTexture(candidate, false);
                        if (direct != null)
                            return direct;
                    }
                }

                FieldInfo field = db.GetType().GetField("databaseTexture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                IEnumerable textures = field != null ? field.GetValue(db) as IEnumerable : null;
                if (textures == null)
                    return null;

                foreach (object item in textures)
                {
                    if (item == null)
                        continue;

                    Type itemType = item.GetType();
                    string texName = ReadMemberStringValue(item, itemType, "name", "Name");
                    if (!LooksLikePortraitTextureName(texName, keys))
                        continue;

                    Texture tex = ReadMemberTexture(item, itemType, "texture", "Texture");
                    if (tex != null)
                        return tex;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:733", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:733", ex); }

            return null;
        }

        private static bool LooksLikePortraitTextureName(string texName, IEnumerable<string> searchKeys)
        {
            if (string.IsNullOrEmpty(texName) || searchKeys == null)
                return false;

            string normalizedTex = NormalizePortraitKey(texName);
            if (normalizedTex.Length == 0)
                return false;

            bool portraitish = normalizedTex.Contains("portrait") || normalizedTex.Contains("avatar");
            if (!portraitish)
                return false;

            foreach (string key in searchKeys)
            {
                string normalizedKey = NormalizePortraitKey(key);
                if (normalizedKey.Length == 0)
                    continue;

                if (normalizedTex.Contains(normalizedKey))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> BuildPortraitTextureCandidates(string portraitKey)
        {
            foreach (string name in BuildPortraitNameVariants(portraitKey))
            {
                yield return "TextureReplacer/PluginData/Portraits/" + name;
                yield return "TextureReplacer/Portraits/" + name;
                yield return "Portraits/" + name;
                yield return "TextureReplacer/PluginData/Portraits/" + name + "/portrait";
                yield return "TextureReplacer/Portraits/" + name + "/portrait";
                yield return "TextureReplacer/Skins/" + name + "/portrait";
                yield return "TextureReplacer/Skins/" + name + "/Portrait";
                yield return "TextureReplacer/Skins/" + name + "/avatar";
                yield return "TextureReplacer/Skins/" + name + "/Avatar";
            }
        }

        private static IEnumerable<string> BuildPortraitNameVariants(string portraitKey)
        {
            string raw = portraitKey ?? string.Empty;
            string trimmed = raw.Trim();
            string noSpaces = trimmed.Replace(" ", string.Empty);
            string underscored = trimmed.Replace(" ", "_");
            string dashed = trimmed.Replace(" ", "-");

            foreach (string value in new[]
            {
                trimmed,
                noSpaces,
                underscored,
                dashed,
                trimmed + "_portrait",
                noSpaces + "_portrait",
                underscored + "_portrait",
                dashed + "_portrait",
                "portrait_" + trimmed,
                "portrait_" + noSpaces,
                "portrait_" + underscored,
                "portrait_" + dashed
            })
            {
                if (!string.IsNullOrEmpty(value))
                    yield return value;
            }
        }

        private static string NormalizePortraitKey(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private Texture TryLoadPortraitTextureFromFiles(IEnumerable<string> searchKeys)
        {
            try
            {
                List<string> keys = searchKeys == null
                    ? new List<string>()
                    : searchKeys.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (keys.Count == 0)
                    return null;

                foreach (string path in BuildPortraitFileCandidates(keys))
                {
                    Texture tex = LoadTextureFromFile(path);
                    if (tex != null)
                        return tex;
                }

                foreach (string dir in EnumeratePortraitSearchDirectories())
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                        continue;

                    try
                    {
                        foreach (string file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                        {
                            string ext = Path.GetExtension(file);
                            if (!IsPortraitImageExtension(ext))
                                continue;

                            if (!LooksLikePortraitMatch(file, keys))
                                continue;

                            Texture tex = LoadTextureFromFile(file);
                            if (tex != null)
                                return tex;
                        }
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:862", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:862", ex); }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.Portraits.cs:865", "Suppressed exception in HallOfHistoryWindow.Portraits.cs:865", ex); }

            return null;
        }

        private IEnumerable<string> BuildPortraitFileCandidates(IEnumerable<string> searchKeys)
        {
            string root = KSPUtil.ApplicationRootPath;
            string saveFolder = HighLogic.SaveFolder ?? string.Empty;
            string[] exts = { ".png", ".jpg", ".jpeg" };
            string[] dirs =
            {
                Path.Combine(root, "GameData", "TextureReplacer", "PluginData", "Portraits"),
                Path.Combine(root, "GameData", "TextureReplacer", "Portraits"),
                Path.Combine(root, "GameData", "TextureReplacerReplaced", "PluginData", "Portraits"),
                Path.Combine(root, "GameData", "TextureReplacerReplaced", "Portraits"),
                Path.Combine(root, "saves", saveFolder, "TextureReplacer", "Portraits"),
                Path.Combine(root, "saves", saveFolder, "TextureReplacer")
            };

            foreach (string key in searchKeys)
            {
                foreach (string name in BuildPortraitNameVariants(key))
                {
                    foreach (string dir in dirs)
                    {
                        if (string.IsNullOrEmpty(dir))
                            continue;

                        foreach (string ext in exts)
                            yield return Path.Combine(dir, name + ext);
                    }
                }
            }
        }

        private IEnumerable<string> EnumeratePortraitSearchDirectories()
        {
            string root = KSPUtil.ApplicationRootPath;
            string saveFolder = HighLogic.SaveFolder ?? string.Empty;

            yield return Path.Combine(root, "GameData", "TextureReplacer");
            yield return Path.Combine(root, "GameData", "TextureReplacer", "Skins");
            yield return Path.Combine(root, "GameData", "TextureReplacerReplaced");
            yield return Path.Combine(root, "GameData", "TextureReplacerReplaced", "Skins");
            yield return Path.Combine(root, "saves", saveFolder, "TextureReplacer");
        }

        private static bool IsPortraitImageExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext))
                return false;

            ext = ext.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        private static bool LooksLikePortraitMatch(string filePath, IEnumerable<string> searchKeys)
        {
            if (string.IsNullOrEmpty(filePath) || searchKeys == null)
                return false;

            string normalizedFile = NormalizePortraitKey(Path.GetFileNameWithoutExtension(filePath));
            string normalizedDir = NormalizePortraitKey(Path.GetDirectoryName(filePath) ?? string.Empty);
            if (normalizedFile.Length == 0 && normalizedDir.Length == 0)
                return false;

            bool portraitish = normalizedFile.Contains("portrait") || normalizedFile.Contains("avatar") ||
                normalizedDir.Contains("portrait") || normalizedDir.Contains("avatar");
            if (!portraitish)
                return false;

            foreach (string key in searchKeys)
            {
                string normalizedKey = NormalizePortraitKey(key);
                if (normalizedKey.Length == 0)
                    continue;

                if (normalizedFile.Contains(normalizedKey) || normalizedDir.Contains(normalizedKey))
                    return true;
            }

            return false;
        }

        private static Texture2D LoadTextureFromFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                    return null;

                Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!TryLoadTextureBytes(tex, bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }

                tex.name = Path.GetFileNameWithoutExtension(path);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryLoadTextureBytes(Texture2D texture, byte[] bytes)
        {
            if (texture == null || bytes == null || bytes.Length == 0)
                return false;

            try
            {
                Type imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (imageConversionType != null)
                {
                    MethodInfo staticLoad = imageConversionType.GetMethod(
                        "LoadImage",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Texture2D), typeof(byte[]) },
                        null);

                    if (staticLoad != null)
                    {
                        object result = staticLoad.Invoke(null, new object[] { texture, bytes });
                        if (result is bool)
                            return (bool)result;
                        return true;
                    }
                }

                MethodInfo instanceLoad = typeof(Texture2D).GetMethod(
                    "LoadImage",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(byte[]) },
                    null);

                if (instanceLoad != null)
                {
                    object result = instanceLoad.Invoke(texture, new object[] { bytes });
                    if (result is bool)
                        return (bool)result;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EAC.HallOfHistory] Texture byte load failed: " + ex.Message);
            }

            return false;
        }

        private static object ReadMemberObject(object obj, Type type, string n1, string n2)
        {
            return ReflectionUtils.GetMemberObject(obj, type, n1, n2);
        }

        private static string ReadMemberStringValue(object obj, Type type, string n1, string n2)
        {
            return ReflectionUtils.GetString(obj, type, n1, n2);
        }

        private static Texture ReadMemberTexture(object obj, Type type, string n1, string n2)
        {
            return ReflectionUtils.GetTexture(obj, type, n1, n2);
        }

        private static Texture ReadMemberTexture(object obj, Type type, string n1, string n2, string n3, string n4)
        {
            return ReflectionUtils.GetTexture(obj, type, n1, n2, n3, n4);
        }
    }
}
