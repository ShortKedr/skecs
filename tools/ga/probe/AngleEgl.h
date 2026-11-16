// Included by GaOpenGlNative.cpp inside its implementation namespace.
using EglHandle = void*;
using EglBool = unsigned;
using EglInt = int;
using EglEnum = unsigned;

constexpr EglInt eglNone = 0x3038, eglVersion = 0x3054, eglExtensions = 0x3055;
constexpr EglEnum eglOpenGlEsApi = 0x30a0, eglPlatformAngle = 0x3202;

struct AnglePciId
{
    uint32_t vendor = 0, device = 0;
    bool operator==(const AnglePciId&) const = default;
};

bool hasEglExtension(const char* extensions, std::string_view name)
{
    if (!extensions)
        return false;
    const std::string_view list(extensions);
    for (auto pos = list.find(name); pos != std::string_view::npos; pos = list.find(name, pos + name.size()))
    {
        const auto end = pos + name.size();
        if ((pos == 0 || list[pos - 1] == ' ') && (end == list.size() || list[end] == ' '))
            return true;
    }
    return false;
}

class AngleModule
{
    HMODULE module;

    static HMODULE open(const std::filesystem::path& path)
    {
        HMODULE existing = nullptr;
        if (GetModuleHandleExW(0, path.filename().c_str(), &existing))
        {
            std::array<wchar_t, 32768> name{};
            const auto length = GetModuleFileNameW(existing, name.data(), static_cast<DWORD>(name.size()));
            FreeLibrary(existing);
            std::error_code error;
            require(length > 0 && length < name.size() && std::filesystem::equivalent(path, name.data(), error),
                "A different ANGLE DLL directory is already loaded");
        }
        const auto loaded = LoadLibraryExW(path.c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!loaded)
        {
            const auto error = GetLastError();
            throw std::runtime_error("Cannot load ANGLE " + path.filename().string()
                + " (Win32 error " + std::to_string(error) + ")");
        }
        return loaded;
    }

public:
    explicit AngleModule(const std::filesystem::path& path) : module(open(path)) {}
    ~AngleModule() { FreeLibrary(module); }
    AngleModule(const AngleModule&) = delete;
    AngleModule& operator=(const AngleModule&) = delete;

    PROC find(const char* name) const { return GetProcAddress(module, name); }

    template<class Function>
    Function bind(const char* name) const
    {
        const auto address = find(name);
        if (!address)
            throw std::runtime_error(std::string("ANGLE EGL export unavailable: ") + name);
        return std::bit_cast<Function>(address);
    }
};

class AngleDisplay
{
    // Keep a packaged Vulkan loader resident until both ANGLE modules are released.
    std::optional<AngleModule> vulkanLoader;
    AngleModule gles, egl;
    EglHandle display = nullptr;
    bool owned = false;

public:
    const GlFunction<PROC, const char*> extension = egl.bind<decltype(extension)>("eglGetProcAddress");
    const GlFunction<EglBool, EglHandle, EglInt*, EglInt*> initialize = egl.bind<decltype(initialize)>("eglInitialize");
    const GlFunction<const char*, EglHandle, EglInt> query = egl.bind<decltype(query)>("eglQueryString");
    const GlFunction<EglBool, EglEnum> bindApi = egl.bind<decltype(bindApi)>("eglBindAPI");
    const GlFunction<EglBool, EglHandle, const EglInt*, EglHandle*, EglInt, EglInt*> choose =
        egl.bind<decltype(choose)>("eglChooseConfig");
    const GlFunction<EglHandle, EglHandle, EglHandle, const EglInt*> makePbuffer =
        egl.bind<decltype(makePbuffer)>("eglCreatePbufferSurface");
    const GlFunction<EglHandle, EglHandle, EglHandle, EglHandle, const EglInt*> create =
        egl.bind<decltype(create)>("eglCreateContext");
    const GlFunction<EglBool, EglHandle, EglHandle, EglHandle, EglHandle> makeCurrent =
        egl.bind<decltype(makeCurrent)>("eglMakeCurrent");
    const GlFunction<EglBool, EglHandle, EglHandle> destroyContext = egl.bind<decltype(destroyContext)>("eglDestroyContext");
    const GlFunction<EglBool, EglHandle, EglHandle> destroySurface = egl.bind<decltype(destroySurface)>("eglDestroySurface");
    const GlFunction<EglBool, EglHandle> terminate = egl.bind<decltype(terminate)>("eglTerminate");
    const GlFunction<EglBool> releaseThread = egl.bind<decltype(releaseThread)>("eglReleaseThread");
    const GlFunction<EglInt> error = egl.bind<decltype(error)>("eglGetError");

    AngleDisplay(const std::filesystem::path& directory, uint32_t renderer, AnglePciId pci)
        : gles(directory / L"libGLESv2.dll"), egl(directory / L"libEGL.dll")
    {
        if (renderer == 1)
        {
            const auto packagedLoader = directory / L"vulkan-1.dll";
            if (std::filesystem::exists(packagedLoader))
                vulkanLoader.emplace(packagedLoader);
        }
        const auto address = extension("eglGetPlatformDisplayEXT");
        require(address != nullptr, "ANGLE eglGetPlatformDisplayEXT unavailable");
        const auto platform = std::bit_cast<GlFunction<EglHandle, EglEnum, void*, const EglInt*>>(address);
        constexpr EglInt backendType = 0x3203, deviceType = 0x3209, hardware = 0x320a;
        constexpr EglInt deviceIdHigh = 0x34d6, deviceIdLow = 0x34d7;
        constexpr EglInt backends[]{0x3208, 0x3450, 0x320d}; // D3D11, Vulkan, OpenGL
        if (pci.vendor)
            require(hasEglExtension(query(nullptr, eglExtensions), "EGL_ANGLE_platform_angle_device_id"),
                "Explicit ANGLE PCI selection requires EGL_ANGLE_platform_angle_device_id");
        const EglInt attributes[]{backendType, backends[renderer], deviceType, hardware, pci.vendor ? deviceIdHigh : eglNone,
            static_cast<EglInt>(pci.vendor), deviceIdLow, static_cast<EglInt>(pci.device), eglNone};
        const auto candidate = platform(eglPlatformAngle, nullptr, attributes);
        check(candidate != nullptr, "eglGetPlatformDisplayEXT(ANGLE hardware)");
        if (!query(candidate, eglVersion))
        {
            error();
            EglInt major = 0, minor = 0;
            check(initialize(candidate, &major, &minor) != 0, "eglInitialize(ANGLE hardware)");
            owned = true;
        }
        display = candidate;
    }

    ~AngleDisplay()
    {
        if (owned)
            terminate(display);
    }

    EglHandle handle() const { return display; }

    void verifyPciId(uint32_t renderer, AnglePciId requested) const
    {
        constexpr EglInt deviceExt = 0x322c;
        using QueryAttribute = GlFunction<EglBool, EglHandle, EglInt, intptr_t*>;
        using QueryDeviceString = GlFunction<const char*, EglHandle, EglInt>;
        const auto displayAttribute = std::bit_cast<QueryAttribute>(extension("eglQueryDisplayAttribEXT"));
        const auto deviceAttribute = std::bit_cast<QueryAttribute>(extension("eglQueryDeviceAttribEXT"));
        const auto deviceString = std::bit_cast<QueryDeviceString>(extension("eglQueryDeviceStringEXT"));
        require(displayAttribute && deviceAttribute && deviceString, "ANGLE EGL device query unavailable");
        intptr_t rawDevice = 0;
        check(displayAttribute(display, deviceExt, &rawDevice) != 0, "eglQueryDisplayAttribEXT(EGL_DEVICE_EXT)");
        require(rawDevice != 0, "ANGLE returned no EGL device for PCI validation");
        const auto device = reinterpret_cast<EglHandle>(rawDevice);
        const auto* extensions = deviceString(device, eglExtensions);
        const auto getAttribute = [&](EglInt attribute, const char* operation)
        {
            intptr_t value = 0;
            check(deviceAttribute(device, attribute, &value) != 0, operation);
            require(value != 0, operation);
            return value;
        };

        AnglePciId actual;
        if (renderer == 0)
        {
            require(hasEglExtension(extensions, "EGL_ANGLE_device_d3d"),
                "ANGLE D3D11 PCI identity requires EGL_ANGLE_device_d3d");
            const auto native = getAttribute(0x33a1, "eglQueryDeviceAttribEXT(EGL_D3D11_DEVICE_ANGLE)");
            const auto d3d = reinterpret_cast<ID3D11Device*>(native);
            Microsoft::WRL::ComPtr<IDXGIDevice> dxgi;
            Microsoft::WRL::ComPtr<IDXGIAdapter> adapter;
            DXGI_ADAPTER_DESC info{};
            require(SUCCEEDED(d3d->QueryInterface(IID_PPV_ARGS(&dxgi))) && SUCCEEDED(dxgi->GetAdapter(&adapter))
                && SUCCEEDED(adapter->GetDesc(&info)), "Could not read the backing ANGLE DXGI adapter identity");
            actual = {info.VendorId, info.DeviceId};
        }
        else
        {
            require(hasEglExtension(extensions, "EGL_ANGLE_device_vulkan"),
                "ANGLE Vulkan PCI identity requires EGL_ANGLE_device_vulkan");
            const auto instance = reinterpret_cast<VkInstance>(
                getAttribute(0x34a9, "eglQueryDeviceAttribEXT(EGL_VULKAN_INSTANCE_ANGLE)"));
            const auto physicalDevice = reinterpret_cast<VkPhysicalDevice>(
                getAttribute(0x34ab, "eglQueryDeviceAttribEXT(EGL_VULKAN_PHYSICAL_DEVICE_ANGLE)"));
            const auto getProcAddress = std::bit_cast<PFN_vkGetInstanceProcAddr>(
                getAttribute(0x34d1, "eglQueryDeviceAttribEXT(EGL_VULKAN_GET_INSTANCE_PROC_ADDR)"));
            const auto properties = std::bit_cast<PFN_vkGetPhysicalDeviceProperties>(
                getProcAddress(instance, "vkGetPhysicalDeviceProperties"));
            require(properties != nullptr, "ANGLE Vulkan device properties function unavailable");
            VkPhysicalDeviceProperties info{};
            properties(physicalDevice, &info);
            actual = {info.vendorID, info.deviceID};
        }
        if (actual != requested)
        {
            char message[128];
            std::snprintf(message, sizeof(message), "Requested ANGLE PCI %04x:%04x but ANGLE selected %04x:%04x",
                requested.vendor, requested.device, actual.vendor, actual.device);
            throw std::runtime_error(message);
        }
    }

    PROC find(const char* name) const
    {
        if (const auto address = gles.find(name))
            return address;
        return extension(name);
    }

    void check(bool success, const char* operation) const
    {
        if (!success)
        {
            char message[160];
            std::snprintf(message, sizeof(message), "%s failed (EGL error 0x%04x)", operation,
                static_cast<unsigned>(error()));
            throw std::runtime_error(message);
        }
    }
};

class AngleContext
{
    const AngleDisplay& egl;
    const EglHandle display = egl.handle();
    EglHandle surface = nullptr, context = nullptr;
    bool current = false;

public:
    explicit AngleContext(const AngleDisplay& egl) : egl(egl) {}

    ~AngleContext()
    {
        if (current)
            egl.makeCurrent(display, nullptr, nullptr, nullptr);
        if (context)
            egl.destroyContext(display, context);
        if (surface)
            egl.destroySurface(display, surface);
        egl.releaseThread();
    }

    void open()
    {
        egl.check(egl.bindApi(eglOpenGlEsApi) != 0, "eglBindAPI(OpenGL ES)");
        constexpr EglInt surfaceType = 0x3033, pbufferBit = 0x0001, renderable = 0x3040, es3Bit = 0x0040;
        constexpr EglInt configAttributes[]{surfaceType, pbufferBit, renderable, es3Bit, eglNone};
        EglHandle config = nullptr;
        EglInt found = 0;
        egl.check(egl.choose(display, configAttributes, &config, 1, &found) != 0, "eglChooseConfig(ES3/PBuffer)");
        require(found == 1 && config != nullptr, "ANGLE EGL ES3 pbuffer configuration unavailable");
        constexpr EglInt pbuffer[]{0x3057, 1, 0x3056, 1, eglNone};
        surface = egl.makePbuffer(display, config, pbuffer);
        egl.check(surface != nullptr, "eglCreatePbufferSurface");
        constexpr EglInt attributes[]{0x3098, 3, 0x30fb, 1, eglNone};
        context = egl.create(display, config, nullptr, attributes);
        egl.check(context != nullptr, "eglCreateContext(OpenGL ES 3.1)");
        egl.check(egl.makeCurrent(display, surface, surface, context) != 0, "eglMakeCurrent(OpenGL ES)");
        current = true;
    }
};
