using System;
using System.Runtime.InteropServices;

namespace OpenPredator.Core.Backends;

public static unsafe class NativeWmi
{
    private static readonly Guid CLSID_WbemLocator = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid IID_IWbemLocator = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoSetProxyBlanket(IntPtr pProxy, uint dwAuthnSvc, uint dwAuthzSvc, IntPtr pServerPrincName, uint dwAuthnLevel, uint dwImpLevel, IntPtr pAuthInfo, uint dwCapabilities);

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SysAllocString(string sz);

    [DllImport("oleaut32.dll")]
    private static extern void SysFreeString(IntPtr bstr);

    [DllImport("oleaut32.dll")]
    private static extern void VariantInit(ref VARIANT pvarg);

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(ref VARIANT pvarg);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int VariantChangeType(out VARIANT pvargDest, ref VARIANT pvarSrc, ushort wFlags, ushort vt);

    [DllImport("oleaut32.dll")]
    private static extern IntPtr SafeArrayCreateVector(ushort vt, int lLbound, uint cElements);

    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayPutElement(IntPtr psa, ref int rgIndices, void* pv);

    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayDestroy(IntPtr psa);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct VARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public ulong ullVal;
        [FieldOffset(8)] public long llVal;
        [FieldOffset(8)] public uint uintVal;
        [FieldOffset(8)] public int intVal;
        [FieldOffset(8)] public IntPtr bstrVal;
        [FieldOffset(8)] public IntPtr parray;
    }

    private static IntPtr _pServices = IntPtr.Zero;
    private static string? _gamingInstancePath = null;
    private static string? _genericInstancePath = null;
    private static readonly object _lock = new();

    public static string LastError { get; set; } = "None";
    public static string GamingInstancePath => _gamingInstancePath ?? "(not resolved)";
    public static string GenericInstancePath => _genericInstancePath ?? "(not resolved)";

    /// <summary>Reset cached WMI connection and instance paths</summary>
    public static void ResetCache()
    {
        lock (_lock)
        {
            _pServices = IntPtr.Zero;
            _gamingInstancePath = null;
            _genericInstancePath = null;
            LastError = "None";
        }
    }

    public static IntPtr GetServices(out string error)
    {
        lock (_lock)
        {
            if (_pServices != IntPtr.Zero)
            {
                error = "OK (Cached)";
                return _pServices;
            }

            try
            {
                CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED

                int hr = CoCreateInstance(in CLSID_WbemLocator, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, in IID_IWbemLocator, out IntPtr pLocator);
                if (hr != 0 || pLocator == IntPtr.Zero)
                {
                    error = $"CoCreateInstance(CLSID_WbemLocator) failed: 0x{hr:X8}";
                    LastError = error;
                    return IntPtr.Zero;
                }

                IntPtr* vtableLocator = *(IntPtr**)pLocator;
                var connectServer = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, out IntPtr, int>)vtableLocator[3];

                // Ghidra PSSvc FUN_140012c30 confirms: namespace is "ROOT\WMI" (uppercase)
                IntPtr bstrNamespace = SysAllocString(@"ROOT\WMI");
                hr = connectServer(pLocator, bstrNamespace, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, out IntPtr pServices);
                SysFreeString(bstrNamespace);

                // Release locator
                var releaseLocator = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtableLocator[2];
                releaseLocator(pLocator);

                if (hr != 0 || pServices == IntPtr.Zero)
                {
                    error = $"ConnectServer(ROOT\\WMI) failed: 0x{hr:X8}";
                    LastError = error;
                    return IntPtr.Zero;
                }

                // SetProxyBlanket (RPC_C_AUTHN_WINNT=10, RPC_C_AUTHZ_NONE=0, RPC_C_AUTHN_LEVEL_CALL=3, RPC_C_IMP_LEVEL_IMPERSONATE=3)
                CoSetProxyBlanket(pServices, 10, 0, IntPtr.Zero, 3, 3, IntPtr.Zero, 0);

                _pServices = pServices;
                error = "OK";
                return _pServices;
            }
            catch (Exception ex)
            {
                error = $"Exception in GetServices: {ex.Message}";
                LastError = error;
                return IntPtr.Zero;
            }
        }
    }

    public static string GetOrFindInstancePath(IntPtr pServices, string className)
    {
        if (className == "AcerGamingFunction" && _gamingInstancePath != null) return _gamingInstancePath;
        if (className == "APGeAction" && _genericInstancePath != null) return _genericInstancePath;

        // Default fallbacks from ACPI PNP0C14 standard (with escaped backslashes for WMI path parser)
        string defaultPath = className == "APGeAction"
            ? @"APGeAction.InstanceName=""ACPI\\PNP0C14\\APGe_0"""
            : $@"{className}.InstanceName=""ACPI\\PNP0C14\\0""";

        try
        {
            IntPtr* vtableServices = *(IntPtr**)pServices;
            // IWbemServices::ExecQuery (slot 20, offset 0xa0 in wbemcli.h)
            // Ghidra PSSvc FUN_140012d50: executes "SELECT * FROM <className>"
            var execQuery = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, out IntPtr, int>)vtableServices[20];
            IntPtr bstrWql = SysAllocString("WQL");
            IntPtr bstrQuery = SysAllocString($"SELECT * FROM {className}");
            int hr = execQuery(pServices, bstrWql, bstrQuery, 0x30 /* WBEM_FLAG_RETURN_IMMEDIATELY | WBEM_FLAG_FORWARD_ONLY */, IntPtr.Zero, out IntPtr pEnum);
            SysFreeString(bstrWql);
            SysFreeString(bstrQuery);

            if (hr != 0 || pEnum == IntPtr.Zero)
            {
                return defaultPath;
            }

            // SetProxyBlanket on enumerator
            CoSetProxyBlanket(pEnum, 10, 0, IntPtr.Zero, 3, 3, IntPtr.Zero, 0);

            // IEnumWbemClassObject::Next (slot 4, offset 0x20)
            IntPtr* vtableEnum = *(IntPtr**)pEnum;
            var next = (delegate* unmanaged[Stdcall]<IntPtr, int, uint, out IntPtr, out uint, int>)vtableEnum[4];
            hr = next(pEnum, -1, 1, out IntPtr pInstance, out uint uReturned);

            // Release enumerator
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtableEnum[2])(pEnum);

            if (hr == 0 && uReturned > 0 && pInstance != IntPtr.Zero)
            {
                IntPtr* vtableInst = *(IntPtr**)pInstance;
                var get = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, out VARIANT, IntPtr, IntPtr, int>)vtableInst[4];

                string? foundPath = null;

                // 1. First try reading the canonical __RELPATH system property (already escaped by WMI)
                IntPtr bstrRelPath = SysAllocString("__RELPATH");
                hr = get(pInstance, bstrRelPath, 0, out VARIANT varRelPath, IntPtr.Zero, IntPtr.Zero);
                SysFreeString(bstrRelPath);

                if (hr == 0 && varRelPath.vt == 8 && varRelPath.bstrVal != IntPtr.Zero)
                {
                    foundPath = Marshal.PtrToStringUni(varRelPath.bstrVal);
                    VariantClear(ref varRelPath);
                }

                // 2. Fallback: read InstanceName property and escape backslashes
                if (string.IsNullOrEmpty(foundPath))
                {
                    IntPtr bstrInstName = SysAllocString("InstanceName");
                    hr = get(pInstance, bstrInstName, 0, out VARIANT varInstName, IntPtr.Zero, IntPtr.Zero);
                    SysFreeString(bstrInstName);

                    if (hr == 0 && varInstName.vt == 8 && varInstName.bstrVal != IntPtr.Zero)
                    {
                        string? instanceName = Marshal.PtrToStringUni(varInstName.bstrVal);
                        VariantClear(ref varInstName);
                        if (!string.IsNullOrEmpty(instanceName))
                        {
                            string escaped = instanceName.Replace(@"\", @"\\");
                            foundPath = $@"{className}.InstanceName=""{escaped}""";
                        }
                    }
                }

                // Release instance
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtableInst[2])(pInstance);

                if (!string.IsNullOrEmpty(foundPath))
                {
                    if (className == "AcerGamingFunction") _gamingInstancePath = foundPath;
                    if (className == "APGeAction") _genericInstancePath = foundPath;
                    return foundPath;
                }
            }
        }
        catch { }

        return defaultPath;
    }

    public static ulong ExecuteMethod(string className, string methodName, string inParamName, ulong inValue, string outParamName)
    {
        IntPtr pServices = GetServices(out _);
        if (pServices == IntPtr.Zero) return 0;

        string instancePath = GetOrFindInstancePath(pServices, className);

        IntPtr bstrClass = SysAllocString(className);
        IntPtr bstrInstancePath = SysAllocString(instancePath);
        IntPtr bstrMethod = SysAllocString(methodName);
        IntPtr bstrInParam = SysAllocString(inParamName);
        IntPtr bstrOutParam = SysAllocString(outParamName);

        IntPtr pClass = IntPtr.Zero;
        IntPtr pInParamsDef = IntPtr.Zero;
        IntPtr pInParams = IntPtr.Zero;
        IntPtr pOutParams = IntPtr.Zero;

        try
        {
            IntPtr* vtableServices = *(IntPtr**)pServices;

            // 1. IWbemServices::GetObject (slot 6) - get class schema
            var getObject = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, out IntPtr, IntPtr, int>)vtableServices[6];
            int hr = getObject(pServices, bstrClass, 0, IntPtr.Zero, out pClass, IntPtr.Zero);
            if (hr != 0 || pClass == IntPtr.Zero)
            {
                LastError = $"GetObject({className}) failed: 0x{hr:X8}";
                return 0;
            }

            // 2. IWbemClassObject::GetMethod (slot 19 in wbemcli.h)
            IntPtr* vtableClass = *(IntPtr**)pClass;
            var getMethod = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, out IntPtr, out IntPtr, int>)vtableClass[19];
            hr = getMethod(pClass, bstrMethod, 0, out pInParamsDef, out IntPtr pOutParamsDef);
            if (pOutParamsDef != IntPtr.Zero)
            {
                IntPtr* vtableOutDef = *(IntPtr**)pOutParamsDef;
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtableOutDef[2])(pOutParamsDef);
            }
            if (hr != 0 || pInParamsDef == IntPtr.Zero)
            {
                LastError = $"GetMethod({methodName}) failed: 0x{hr:X8}";
                return 0;
            }

            // 3. IWbemClassObject::SpawnInstance (slot 15 in wbemcli.h) to create parameter instance
            IntPtr* vtableInDef = *(IntPtr**)pInParamsDef;
            var spawnInstance = (delegate* unmanaged[Stdcall]<IntPtr, int, out IntPtr, int>)vtableInDef[15];
            int hrSpawn = spawnInstance(pInParamsDef, 0, out pInParams);
            if (hrSpawn != 0 || pInParams == IntPtr.Zero)
            {
                // Fallback to definition if spawn fails
                pInParams = pInParamsDef;
            }

            // Check expected CIMTYPE for inParamName
            IntPtr* vtableIn = *(IntPtr**)pInParams;
            var getParamDef = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, out VARIANT, out int, IntPtr, int>)vtableIn[4];
            int hrGetDef = getParamDef(pInParams, bstrInParam, 0, out VARIANT varSample, out int cimType, IntPtr.Zero);
            VariantClear(ref varSample);

            // 4. IWbemClassObject::Put (slot 5)
            var put = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, ref VARIANT, int, int>)vtableIn[5];

            var varInput = new VARIANT();
            VariantInit(ref varInput);

            IntPtr psa = IntPtr.Zero;
            if (methodName.Contains("KBBacklight") || methodName.Contains("Kbbacklight"))
            {
                // Acer WMI driver expects a 16-byte SAFEARRAY buffer for keyboard backlight
                // Ghidra decompilation of PSSvc FUN_140013430:
                // byte 0..7 = inValue bytes, byte 8 = 0, byte 9 = 1, byte 10..15 = 0
                psa = SafeArrayCreateVector(17 /* VT_UI1 */, 0, 16);
                byte[] bytes = BitConverter.GetBytes(inValue);
                for (int idx = 0; idx < 16; idx++)
                {
                    byte b = idx < bytes.Length ? bytes[idx] : (byte)0;
                    if (idx == 9)
                    {
                        b = 1;
                    }
                    SafeArrayPutElement(psa, ref idx, &b);
                }
                varInput.vt = 0x2011; // VT_ARRAY | VT_UI1
                varInput.parray = psa;
                hr = put(pInParams, bstrInParam, 0, ref varInput, 0);
                if (hr != 0)
                {
                    VariantClear(ref varInput);
                    varInput.vt = 21; // VT_UI8
                    varInput.ullVal = inValue;
                    hr = put(pInParams, bstrInParam, 0, ref varInput, 0);
                }
            }
            else
            {
                // Set parameter matching CIMTYPE or standard types
                if (cimType == 21) // CIM_UINT64
                {
                    varInput.vt = 21; // VT_UI8
                    varInput.ullVal = inValue;
                }
                else if (cimType == 19) // CIM_UINT32
                {
                    varInput.vt = 19; // VT_UI4
                    varInput.uintVal = (uint)inValue;
                }
                else if (cimType == 3) // CIM_SINT32
                {
                    varInput.vt = 3; // VT_I4
                    varInput.intVal = (int)(uint)inValue;
                }
                else if (cimType == 8) // CIM_STRING
                {
                    varInput.vt = 8; // VT_BSTR
                    varInput.bstrVal = SysAllocString(inValue.ToString());
                }
                else
                {
                    // Default: try VT_UI4 for 32-bit values, VT_UI8 for 64-bit
                    if (inValue <= uint.MaxValue)
                    {
                        varInput.vt = 19; // VT_UI4
                        varInput.uintVal = (uint)inValue;
                    }
                    else
                    {
                        varInput.vt = 21; // VT_UI8
                        varInput.ullVal = inValue;
                    }
                }

                hr = put(pInParams, bstrInParam, 0, ref varInput, 0);

                if (hr != 0)
                {
                    // Fallback 1: try VT_BSTR (some Acer BIOS MOFs represent uint inputs as string)
                    VariantClear(ref varInput);
                    varInput.vt = 8; // VT_BSTR
                    varInput.bstrVal = SysAllocString(inValue.ToString());
                    hr = put(pInParams, bstrInParam, 0, ref varInput, 0);
                }

                if (hr != 0)
                {
                    // Fallback 2: try VT_UI8
                    VariantClear(ref varInput);
                    varInput.vt = 21; // VT_UI8
                    varInput.ullVal = inValue;
                    hr = put(pInParams, bstrInParam, 0, ref varInput, 0);
                }

                if (hr != 0)
                {
                    // Fallback 3: try VT_UI4
                    VariantClear(ref varInput);
                    varInput.vt = 19; // VT_UI4
                    varInput.uintVal = (uint)inValue;
                    hr = put(pInParams, bstrInParam, 0, ref varInput, 0);
                }
            }

            if (hr != 0)
            {
                LastError = $"Put({inParamName}, cimType={cimType}) failed: 0x{hr:X8}";
            }
            VariantClear(ref varInput);

            // 5. IWbemServices::ExecMethod (slot 24) on the INSTANCE path
            var execMethod = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, out IntPtr, IntPtr, int>)vtableServices[24];
            hr = execMethod(pServices, bstrInstancePath, bstrMethod, 0, IntPtr.Zero, pInParams, out pOutParams, IntPtr.Zero);
            if (hr != 0 || pOutParams == IntPtr.Zero)
            {
                int hrInst = hr;
                // If instance path failed, try class path fallback
                if (instancePath != className)
                {
                    hr = execMethod(pServices, bstrClass, bstrMethod, 0, IntPtr.Zero, pInParams, out pOutParams, IntPtr.Zero);
                }
                if (hr != 0 || pOutParams == IntPtr.Zero)
                {
                    // If spawned instance failed, try once more with definition pointer pInParamsDef
                    if (pInParams != pInParamsDef)
                    {
                        hr = execMethod(pServices, bstrInstancePath, bstrMethod, 0, IntPtr.Zero, pInParamsDef, out pOutParams, IntPtr.Zero);
                    }
                    if (hr != 0 || pOutParams == IntPtr.Zero)
                    {
                        LastError = $"ExecMethod({instancePath}->{methodName}) failed: hrInst=0x{hrInst:X8}, hrClass=0x{hr:X8}";
                        return 0;
                    }
                }
            }

            // 6. IWbemClassObject::Get (slot 4) on ppOutParams
            IntPtr* vtableOut = *(IntPtr**)pOutParams;
            var get = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, out VARIANT, out int, IntPtr, int>)vtableOut[4];

            var varOut = new VARIANT();
            int outCimType = 0;
            hr = get(pOutParams, bstrOutParam, 0, out varOut, out outCimType, IntPtr.Zero);
            if (hr == 0)
            {
                // Try converting output variant to VT_UI8 (0x15 = 21) matching PSSvc FUN_140013bc0
                var varDest = new VARIANT();
                VariantInit(ref varDest);
                int hrConv = VariantChangeType(out varDest, ref varOut, 0, 21 /* VT_UI8 */);
                ulong result;
                if (hrConv == 0)
                {
                    result = varDest.ullVal;
                    VariantClear(ref varDest);
                }
                else
                {
                    result = varOut.vt switch
                    {
                        19 => varOut.uintVal,               // VT_UI4
                        21 => varOut.ullVal,                // VT_UI8
                        3 => (ulong)(uint)varOut.intVal,    // VT_I4
                        20 => (ulong)varOut.llVal,          // VT_I8
                        8 => ParseBstrToUlong(varOut.bstrVal), // VT_BSTR
                        _ => varOut.ullVal
                    };
                }
                VariantClear(ref varOut);
                LastError = $"Success (0x{result:X})";
                return result;
            }
            else
            {
                LastError = $"Get({outParamName}) failed: 0x{hr:X8}";
            }

            return 0;
        }
        catch (Exception ex)
        {
            LastError = $"Exception: {ex.Message}";
            return 0;
        }
        finally
        {
            if (pOutParams != IntPtr.Zero)
            {
                IntPtr* vtable = *(IntPtr**)pOutParams;
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2])(pOutParams);
            }
            if (pInParams != IntPtr.Zero && pInParams != pInParamsDef)
            {
                IntPtr* vtable = *(IntPtr**)pInParams;
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2])(pInParams);
            }
            if (pInParamsDef != IntPtr.Zero)
            {
                IntPtr* vtable = *(IntPtr**)pInParamsDef;
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2])(pInParamsDef);
            }
            if (pClass != IntPtr.Zero)
            {
                IntPtr* vtable = *(IntPtr**)pClass;
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2])(pClass);
            }

            SysFreeString(bstrClass);
            SysFreeString(bstrInstancePath);
            SysFreeString(bstrMethod);
            SysFreeString(bstrInParam);
            SysFreeString(bstrOutParam);
        }
    }

    public static ulong ExecuteGenericMethod(string methodName, ulong input)
    {
        return ExecuteMethod("APGeAction", methodName, "uiInput", input, "uiOutput");
    }

    public static ulong ExecuteGamingFunction(string methodName, ulong input)
    {
        return ExecuteMethod("AcerGamingFunction", methodName, "gmInput", input, "gmOutput");
    }

    private static ulong ParseBstrToUlong(IntPtr bstr)
    {
        if (bstr == IntPtr.Zero) return 0;
        string? s = Marshal.PtrToStringUni(bstr)?.Trim();
        if (string.IsNullOrEmpty(s)) return 0;

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out ulong hexVal))
                return hexVal;
        }
        if (ulong.TryParse(s, out ulong decVal))
            return decVal;

        return 0;
    }
}

