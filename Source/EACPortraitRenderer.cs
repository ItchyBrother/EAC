using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RosterRotation
{
    internal static class EACPortraitRenderer
    {
        private const int PortraitSize = 256;

        public static bool TryCapturePortrait(ProtoCrewMember pcm, out string detail, out string savedPath)
        {
            detail = null;
            savedPath = null;

            if (pcm == null || string.IsNullOrEmpty(pcm.name))
            {
                detail = "pcm=null";
                return false;
            }

            if (HallOfHistoryWindow.HasCapturedPortraitCache(pcm.name))
            {
                savedPath = HallOfHistoryWindow.GetPrimaryPortraitCachePath(pcm.name);
                detail = "already-cached";
                return true;
            }

            PortraitSession session = null;
            try
            {
                if (!TryBuildOffscreenSession(pcm, out session, out detail) || session == null || session.OutputTexture == null)
                    return false;

                bool saved = HallOfHistoryWindow.TryCapturePortraitCache(pcm.name, session.OutputTexture);
                if (!saved)
                {
                    detail = AppendDetail(detail, "save=false");
                    return false;
                }

                savedPath = HallOfHistoryWindow.GetPrimaryPortraitCachePath(pcm.name);
                detail = AppendDetail(detail, "save=true");
                return true;
            }
            catch (Exception ex)
            {
                detail = AppendDetail(detail, ex.GetType().Name + ":" + ex.Message);
                return false;
            }
            finally
            {
                CleanupSession(session);
            }
        }

        private static bool TryBuildOffscreenSession(ProtoCrewMember pcm, out PortraitSession session, out string detail)
        {
            session = null;
            detail = null;

            if (TryDirectTextureProvider(pcm, out session, out detail))
                return true;

            if (TryFactoryProvider(pcm, out session, out detail))
                return true;

            detail = AppendDetail(detail, "no-offscreen-provider");
            return false;
        }

        private static bool TryDirectTextureProvider(ProtoCrewMember pcm, out PortraitSession session, out string detail)
        {
            session = null;
            detail = null;

            foreach (Type type in GetPortraitCandidateTypes())
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                catch { continue; }
                if (methods == null)
                    continue;

                foreach (MethodInfo method in methods)
                {
                    if (method == null || method.ReturnType == typeof(void) || !typeof(Texture).IsAssignableFrom(method.ReturnType))
                        continue;

                    string methodName = method.Name ?? string.Empty;
                    if (methodName.IndexOf("get", StringComparison.OrdinalIgnoreCase) < 0 &&
                        methodName.IndexOf("render", StringComparison.OrdinalIgnoreCase) < 0 &&
                        methodName.IndexOf("create", StringComparison.OrdinalIgnoreCase) < 0 &&
                        methodName.IndexOf("generate", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    PortraitSession candidate = new PortraitSession();
                    object[] args;
                    if (!TryBuildInvocationArgs(method.GetParameters(), pcm, candidate, out args))
                    {
                        CleanupSession(candidate);
                        continue;
                    }

                    try
                    {
                        Texture tex = method.Invoke(null, args) as Texture;
                        if (tex == null)
                        {
                            CleanupSession(candidate);
                            continue;
                        }

                        candidate.OutputTexture = tex;
                        session = candidate;
                        detail = type.FullName + "." + method.Name + "[direct]";
                        return true;
                    }
                    catch
                    {
                        CleanupSession(candidate);
                    }
                }
            }

            return false;
        }

        private static bool TryFactoryProvider(ProtoCrewMember pcm, out PortraitSession session, out string detail)
        {
            session = null;
            detail = null;

            foreach (Type type in GetPortraitCandidateTypes())
            {
                object handler;
                PortraitSession candidate;
                string route;

                if (TryCreateViaConstructors(type, pcm, out handler, out candidate, out route) ||
                    TryCreateViaStaticFactories(type, pcm, out handler, out candidate, out route))
                {
                    if (handler == null)
                    {
                        CleanupSession(candidate);
                        continue;
                    }

                    candidate.HandlerObject = handler;
                    if (candidate.OwnedUnityObject == null)
                    {
                        var unityObj = handler as UnityEngine.Object;
                        if (unityObj != null)
                            candidate.OwnedUnityObject = unityObj;
                    }

                    if (TryHarvestTextureFromHandler(candidate, handler))
                    {
                        session = candidate;
                        detail = route;
                        return true;
                    }

                    CleanupSession(candidate);
                }
            }

            return false;
        }

        private static bool TryCreateViaConstructors(Type type, ProtoCrewMember pcm, out object handler, out PortraitSession session, out string route)
        {
            handler = null;
            session = null;
            route = null;

            ConstructorInfo[] ctors;
            try { ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
            catch { return false; }
            if (ctors == null)
                return false;

            foreach (ConstructorInfo ctor in ctors)
            {
                if (ctor == null)
                    continue;

                PortraitSession candidate = new PortraitSession();
                object[] args;
                if (!TryBuildInvocationArgs(ctor.GetParameters(), pcm, candidate, out args))
                {
                    CleanupSession(candidate);
                    continue;
                }

                try
                {
                    handler = ctor.Invoke(args);
                    if (handler == null)
                    {
                        CleanupSession(candidate);
                        continue;
                    }

                    session = candidate;
                    route = type.FullName + ".ctor";
                    return true;
                }
                catch
                {
                    CleanupSession(candidate);
                }
            }

            return false;
        }

        private static bool TryCreateViaStaticFactories(Type type, ProtoCrewMember pcm, out object handler, out PortraitSession session, out string route)
        {
            handler = null;
            session = null;
            route = null;

            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
            catch { return false; }
            if (methods == null)
                return false;

            foreach (MethodInfo method in methods)
            {
                if (method == null || method.ReturnType == typeof(void))
                    continue;

                string methodName = method.Name ?? string.Empty;
                if (methodName.IndexOf("create", StringComparison.OrdinalIgnoreCase) < 0 &&
                    methodName.IndexOf("instantiate", StringComparison.OrdinalIgnoreCase) < 0 &&
                    methodName.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) < 0 &&
                    methodName.IndexOf("build", StringComparison.OrdinalIgnoreCase) < 0 &&
                    methodName.IndexOf("make", StringComparison.OrdinalIgnoreCase) < 0 &&
                    methodName.IndexOf("generate", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                PortraitSession candidate = new PortraitSession();
                object[] args;
                if (!TryBuildInvocationArgs(method.GetParameters(), pcm, candidate, out args))
                {
                    CleanupSession(candidate);
                    continue;
                }

                try
                {
                    handler = method.Invoke(null, args);
                    if (handler == null)
                    {
                        CleanupSession(candidate);
                        continue;
                    }

                    session = candidate;
                    route = type.FullName + "." + method.Name;
                    return true;
                }
                catch
                {
                    CleanupSession(candidate);
                }
            }

            return false;
        }

        private static bool TryHarvestTextureFromHandler(PortraitSession session, object handler)
        {
            if (session == null || handler == null)
                return false;

            Texture directTexture = handler as Texture;
            if (directTexture != null)
            {
                session.OutputTexture = directTexture;
                return true;
            }

            Type handlerType = handler.GetType();

            Texture tex = ReadTextureMember(handler, handlerType,
                "PortraitTexture", "portraitTexture", "texture", "Texture", "targetTexture", "TargetTexture", "RenderTexture", "renderTexture");
            if (tex != null)
            {
                session.OutputTexture = tex;
                return true;
            }

            RenderTexture rt = EnsureRenderTexture(session);
            TrySetRenderTarget(handler, handlerType, rt);
            TryInvokeCommonLifecycle(handler, handlerType);

            tex = ReadTextureMember(handler, handlerType,
                "PortraitTexture", "portraitTexture", "texture", "Texture", "targetTexture", "TargetTexture", "RenderTexture", "renderTexture");
            if (tex != null)
            {
                session.OutputTexture = tex;
                return true;
            }

            GameObject go = GetGameObject(handler);
            if (go != null)
            {
                Camera[] cameras = null;
                try { cameras = go.GetComponentsInChildren<Camera>(true); }
                catch { cameras = null; }

                if (cameras != null)
                {
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        Camera cam = cameras[i];
                        if (cam == null)
                            continue;

                        try
                        {
                            cam.targetTexture = rt;
                            cam.Render();
                            session.OutputTexture = rt;
                            return true;
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:332", "Suppressed exception in EACPortraitRenderer.cs:332", ex); }
                    }
                }
            }

            Camera singleCamera = ReadObjectMember(handler, handlerType, "camera", "Camera", "portraitCamera", "PortraitCamera") as Camera;
            if (singleCamera != null)
            {
                try
                {
                    singleCamera.targetTexture = rt;
                    singleCamera.Render();
                    session.OutputTexture = rt;
                    return true;
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:347", "Suppressed exception in EACPortraitRenderer.cs:347", ex); }
            }

            return false;
        }

        private static bool TryBuildInvocationArgs(ParameterInfo[] parameters, ProtoCrewMember pcm, PortraitSession session, out object[] args)
        {
            args = null;
            if (parameters == null)
            {
                args = new object[0];
                return true;
            }

            args = new object[parameters.Length];
            Type pcmType = pcm.GetType();

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo p = parameters[i];
                Type pt = p.ParameterType;
                string pname = (p.Name ?? string.Empty).ToLowerInvariant();

                if (pt.IsAssignableFrom(pcmType))
                {
                    args[i] = pcm;
                    continue;
                }

                if (pt == typeof(string))
                {
                    args[i] = pcm.name;
                    continue;
                }

                if (typeof(Texture).IsAssignableFrom(pt) || typeof(RenderTexture).IsAssignableFrom(pt))
                {
                    args[i] = EnsureRenderTexture(session);
                    continue;
                }

                if (pt == typeof(int))
                {
                    args[i] = (pname.Contains("size") || pname.Contains("width") || pname.Contains("height")) ? PortraitSize : 0;
                    continue;
                }

                if (pt == typeof(float))
                {
                    args[i] = 0f;
                    continue;
                }

                if (pt == typeof(bool))
                {
                    args[i] = false;
                    continue;
                }

                if (pt.IsEnum)
                {
                    Array values = Enum.GetValues(pt);
                    args[i] = values != null && values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(pt);
                    continue;
                }

                if (!pt.IsValueType)
                {
                    args[i] = null;
                    continue;
                }

                if (p.IsOptional)
                {
                    args[i] = p.DefaultValue;
                    continue;
                }

                try
                {
                    args[i] = Activator.CreateInstance(pt);
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        private static RenderTexture EnsureRenderTexture(PortraitSession session)
        {
            if (session.OwnedRenderTexture == null)
            {
                session.OwnedRenderTexture = new RenderTexture(PortraitSize, PortraitSize, 24, RenderTextureFormat.ARGB32);
                session.OwnedRenderTexture.Create();
            }

            return session.OwnedRenderTexture;
        }

        private static void TrySetRenderTarget(object handler, Type type, RenderTexture rt)
        {
            if (handler == null || type == null || rt == null)
                return;

            string[] names = { "SetTexture", "SetPortraitTexture", "SetRenderTexture", "SetTargetTexture", "AssignTexture", "AssignRenderTexture" };
            for (int i = 0; i < names.Length; i++)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
                catch { continue; }
                if (methods == null)
                    continue;

                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo method = methods[m];
                    if (method == null || !string.Equals(method.Name, names[i], StringComparison.OrdinalIgnoreCase))
                        continue;

                    ParameterInfo[] ps = method.GetParameters();
                    if (ps == null || ps.Length != 1)
                        continue;

                    Type pt = ps[0].ParameterType;
                    if (!typeof(Texture).IsAssignableFrom(pt) && !typeof(RenderTexture).IsAssignableFrom(pt))
                        continue;

                    try { method.Invoke(handler, new object[] { rt }); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:478", "Suppressed exception in EACPortraitRenderer.cs:478", ex); }
                }
            }

            WriteTextureMember(handler, type, rt,
                "targetTexture", "TargetTexture", "RenderTexture", "renderTexture", "texture", "Texture");
        }

        private static void TryInvokeCommonLifecycle(object handler, Type type)
        {
            if (handler == null || type == null)
                return;

            string[] names = { "Awake", "Start", "Setup", "Refresh", "Update", "LateUpdate", "Render", "Rebuild", "ForceRefresh" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    MethodInfo method = type.GetMethod(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (method != null)
                        method.Invoke(handler, null);
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:500", "Suppressed exception in EACPortraitRenderer.cs:500", ex); }
            }
        }

        private static IEnumerable<Type> GetPortraitCandidateTypes()
        {
            var list = new List<Type>();
            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { yield break; }
            if (assemblies == null)
                yield break;

            for (int a = 0; a < assemblies.Length; a++)
            {
                Assembly asm = assemblies[a];
                if (asm == null)
                    continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                if (types == null)
                    continue;

                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null)
                        continue;

                    string name = (type.FullName ?? type.Name ?? string.Empty).ToLowerInvariant();
                    if (name.IndexOf("portrait", StringComparison.Ordinal) < 0)
                        continue;

                    if (!ContainsProtoCrewMemberBinding(type))
                        continue;

                    list.Add(type);
                }
            }

            list.Sort(CompareCandidateTypes);
            for (int i = 0; i < list.Count; i++)
                yield return list[i];
        }

        private static int CompareCandidateTypes(Type a, Type b)
        {
            return GetCandidateScore(b).CompareTo(GetCandidateScore(a));
        }

        private static int GetCandidateScore(Type type)
        {
            if (type == null)
                return 0;

            string name = (type.FullName ?? type.Name ?? string.Empty).ToLowerInvariant();
            int score = 0;
            if (name.Contains("kerbal")) score += 40;
            if (name.Contains("portrait")) score += 30;
            if (name.Contains("gallery")) score -= 15;
            if (name.Contains("list")) score -= 5;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) score += 10;
            return score;
        }

        private static bool ContainsProtoCrewMemberBinding(Type type)
        {
            try
            {
                ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (ctors != null)
                {
                    for (int i = 0; i < ctors.Length; i++)
                    {
                        if (HasProtoCrewMemberParameter(ctors[i].GetParameters()))
                            return true;
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:582", "Suppressed exception in EACPortraitRenderer.cs:582", ex); }

            try
            {
                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (methods != null)
                {
                    for (int i = 0; i < methods.Length; i++)
                    {
                        if (HasProtoCrewMemberParameter(methods[i].GetParameters()))
                            return true;
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:596", "Suppressed exception in EACPortraitRenderer.cs:596", ex); }

            return false;
        }

        private static bool HasProtoCrewMemberParameter(ParameterInfo[] parameters)
        {
            if (parameters == null)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type pt = parameters[i].ParameterType;
                if (pt == typeof(ProtoCrewMember) || pt.IsAssignableFrom(typeof(ProtoCrewMember)))
                    return true;
            }

            return false;
        }

        private static string AppendDetail(string head, string tail)
        {
            if (string.IsNullOrEmpty(tail))
                return head;
            if (string.IsNullOrEmpty(head))
                return tail;
            return head + " | " + tail;
        }

        private static Texture ReadTextureMember(object obj, Type type, params string[] names)
        {
            object value = ReadObjectMember(obj, type, names);
            return value as Texture;
        }

        private static object ReadObjectMember(object obj, Type type, params string[] names)
        {
            if (obj == null || type == null || names == null)
                return null;

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    try { return p.GetValue(obj, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:645", "Suppressed exception in EACPortraitRenderer.cs:645", ex); }
                }

                FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    try { return f.GetValue(obj); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:651", "Suppressed exception in EACPortraitRenderer.cs:651", ex); }
                }
            }

            return null;
        }

        private static void WriteTextureMember(object obj, Type type, Texture tex, params string[] names)
        {
            if (obj == null || type == null || tex == null || names == null)
                return;

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                try
                {
                    PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanWrite && p.PropertyType.IsAssignableFrom(tex.GetType()))
                    {
                        p.SetValue(obj, tex, null);
                        return;
                    }
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:678", "Suppressed exception in EACPortraitRenderer.cs:678", ex); }

                try
                {
                    FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.FieldType.IsAssignableFrom(tex.GetType()))
                    {
                        f.SetValue(obj, tex);
                        return;
                    }
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:689", "Suppressed exception in EACPortraitRenderer.cs:689", ex); }
            }
        }

        private static GameObject GetGameObject(object obj)
        {
            if (obj == null)
                return null;

            GameObject go = obj as GameObject;
            if (go != null)
                return go;

            Component component = obj as Component;
            if (component != null)
                return component.gameObject;

            return null;
        }

        private static void CleanupSession(PortraitSession session)
        {
            if (session == null)
                return;

            try
            {
                if (session.OwnedUnityObject != null)
                {
                    Component component = session.OwnedUnityObject as Component;
                    if (component != null && component.gameObject != null)
                        UnityEngine.Object.Destroy(component.gameObject);
                    else
                        UnityEngine.Object.Destroy(session.OwnedUnityObject);
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:725", "Suppressed exception in EACPortraitRenderer.cs:725", ex); }

            try
            {
                if (session.OwnedRenderTexture != null)
                {
                    session.OwnedRenderTexture.Release();
                    UnityEngine.Object.Destroy(session.OwnedRenderTexture);
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EACPortraitRenderer.cs:735", "Suppressed exception in EACPortraitRenderer.cs:735", ex); }
        }

        private sealed class PortraitSession
        {
            public Texture OutputTexture;
            public RenderTexture OwnedRenderTexture;
            public UnityEngine.Object OwnedUnityObject;
            public object HandlerObject;
        }
    }
}
