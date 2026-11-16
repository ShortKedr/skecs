class NativeContext
{
    EGLDisplay display = EGL_NO_DISPLAY;
    EGLSurface surface = EGL_NO_SURFACE;
    EGLContext context = EGL_NO_CONTEXT;
    bool initialized = false;
    bool current = false;

    static void check(bool succeeded, const char* operation)
    {
        if (!succeeded)
        {
            char message[160];
            std::snprintf(message, sizeof(message), "%s failed (Android EGL error 0x%04x)", operation,
                static_cast<unsigned int>(eglGetError()));
            throw std::runtime_error(message);
        }
    }

public:
    void open()
    {
        display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
        check(display != EGL_NO_DISPLAY, "eglGetDisplay");
        EGLint major = 0, minor = 0;
        check(eglInitialize(display, &major, &minor) == EGL_TRUE, "eglInitialize");
        initialized = true;
        check(eglBindAPI(EGL_OPENGL_ES_API) == EGL_TRUE, "eglBindAPI(OpenGL ES)");

        constexpr EGLint configAttributes[]{
            EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT_KHR, EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_NONE
        };
        EGLConfig config = nullptr;
        EGLint found = 0;
        check(eglChooseConfig(display, configAttributes, &config, 1, &found) == EGL_TRUE, "eglChooseConfig");
        require(found == 1, "Android EGL has no OpenGL ES 3 pbuffer configuration");
        constexpr EGLint size[]{EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE};
        surface = eglCreatePbufferSurface(display, config, size);
        check(surface != EGL_NO_SURFACE, "eglCreatePbufferSurface");

        constexpr EGLint es31[]{EGL_CONTEXT_MAJOR_VERSION_KHR, 3, EGL_CONTEXT_MINOR_VERSION_KHR, 1, EGL_NONE};
        constexpr EGLint es3[]{EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE};
        const auto* raw = eglQueryString(display, EGL_EXTENSIONS);
        const auto extensions = raw ? " " + std::string(raw) + " " : std::string{};
        const bool preciseVersion = major > 1 || (major == 1 && minor >= 5)
            || extensions.find(" EGL_KHR_create_context ") != std::string::npos;
        context = eglCreateContext(display, config, EGL_NO_CONTEXT, preciseVersion ? es31 : es3);
        check(context != EGL_NO_CONTEXT, "eglCreateContext(OpenGL ES 3.1)");
        check(eglMakeCurrent(display, surface, surface, context) == EGL_TRUE, "eglMakeCurrent");
        current = true;
    }

    ~NativeContext()
    {
        if (current)
            eglMakeCurrent(display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        if (context != EGL_NO_CONTEXT)
            eglDestroyContext(display, context);
        if (surface != EGL_NO_SURFACE)
            eglDestroySurface(display, surface);
        // Android system EGL reference counts each successful eglInitialize on a shared display.
        if (initialized)
            eglTerminate(display);
        eglReleaseThread();
    }
};
