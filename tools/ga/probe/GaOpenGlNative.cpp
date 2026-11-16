#include "GaOpenGlNative.h"

#include <windows.h>
#include <GL/gl.h>

#include <algorithm>
#include <array>
#include <bit>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <condition_variable>
#include <cctype>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <future>
#include <memory>
#include <mutex>
#include <optional>
#include <span>


#include <limits>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace
{
#include "StartupGlsl.h"
#include "StartupGlslEs.h"

static_assert(sizeof(GaOpenGlDevice) == 668);

template<class Result, class... Arguments>
using GlFunction = Result(APIENTRY*)(Arguments...);
using Sync = void*;
using BufferSize = std::ptrdiff_t;
using Clock = std::chrono::steady_clock;

constexpr GLenum computeShader = 0x91B9, compileStatus = 0x8B81, linkStatus = 0x8B82, infoLogLength = 0x8B84;
constexpr GLenum shaderStorageBuffer = 0x90D2, dynamicCopy = 0x88EA;
constexpr GLenum computeWorkgroupSize = 0x8267, shaderStorageBlock = 0x92E6, bufferVariable = 0x92E5;
constexpr GLenum activeResources = 0x92F5, bufferBinding = 0x9302, activeVariables = 0x9305;
constexpr GLenum numActiveVariables = 0x9304, variableType = 0x92FA, variableOffset = 0x92FC, arrayStride = 0x92FE;
constexpr GLenum contextMajor = 0x821B, contextMinor = 0x821C;
constexpr GLenum maxGroups = 0x91BE, maxGroupSize = 0x91BF, maxInvocations = 0x90EB;
constexpr GLenum maxSsboBytes = 0x90DE, maxSsboBindings = 0x90DD, maxComputeSsboBlocks = 0x90DB;
constexpr GLenum syncGpuCommandsComplete = 0x9117, alreadySignaled = 0x911A, timeoutExpired = 0x911B;
constexpr GLenum conditionSatisfied = 0x911C;
constexpr GLbitfield bufferUpdateBarrier = 0x0200, syncFlushCommands = 0x0001;

void require(bool condition, std::string_view message)
{
    if (!condition)
        throw std::runtime_error(std::string(message));
}

template<class Function>
Function bindWgl(const char* name)
{
    const auto raw = wglGetProcAddress(name);
    const auto address = std::bit_cast<uintptr_t>(raw);
    if (address > 3 && address != static_cast<uintptr_t>(-1))
        return std::bit_cast<Function>(raw);
    throw std::runtime_error(std::string("OpenGL function unavailable: ") + name);
}

#include "AngleEgl.h"

struct Window
{
    HWND hwnd = nullptr;
    HDC dc = nullptr;
    HGLRC bootstrap = nullptr, core = nullptr;
    HDC previousDc = wglGetCurrentDC();
    HGLRC previousContext = wglGetCurrentContext();

    ~Window()
    {
        if (wglGetCurrentContext() != previousContext && !wglMakeCurrent(previousDc, previousContext))
            wglMakeCurrent(nullptr, nullptr);
        if (core)
            wglDeleteContext(core);
        if (bootstrap)
            wglDeleteContext(bootstrap);
        if (dc)
            ReleaseDC(hwnd, dc);
        if (hwnd)
            DestroyWindow(hwnd);
    }

    void open()
    {
        const auto instance = GetModuleHandleW(nullptr);
        constexpr auto className = L"SkecsGaWglProbe";
        WNDCLASSW windowClass{};
        windowClass.style = CS_OWNDC;
        windowClass.lpfnWndProc = DefWindowProcW;
        windowClass.hInstance = instance;
        windowClass.lpszClassName = className;
        require(RegisterClassW(&windowClass) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS,
            "OpenGL RegisterClassW failed");
        hwnd = CreateWindowExW(0, className, L"", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, instance, nullptr);
        require(hwnd != nullptr, "hidden CreateWindowExW failed");
        dc = GetDC(hwnd);
        require(dc != nullptr, "GetDC failed");

        PIXELFORMATDESCRIPTOR pixel{};
        pixel.nSize = sizeof(pixel);
        pixel.nVersion = 1;
        pixel.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        pixel.iPixelType = PFD_TYPE_RGBA;
        pixel.cColorBits = 24;
        const int format = ChoosePixelFormat(dc, &pixel);
        require(format > 0 && SetPixelFormat(dc, format, &pixel), "OpenGL pixel format unavailable");
        require(DescribePixelFormat(dc, format, sizeof(pixel), &pixel) != 0, "DescribePixelFormat failed");
        require((pixel.dwFlags & PFD_GENERIC_FORMAT) == 0, "OpenGL requires a hardware WGL pixel format");
        bootstrap = wglCreateContext(dc);
        require(bootstrap != nullptr && wglMakeCurrent(dc, bootstrap), "WGL bootstrap context unavailable");
    }

    void upgrade()
    {
        const auto createContext = bindWgl<GlFunction<HGLRC, HDC, HGLRC, const int*>>("wglCreateContextAttribsARB");
        constexpr int major = 0x2091, minor = 0x2092, profile = 0x9126, coreBit = 0x1;
        constexpr int attributes[]{major, 4, minor, 3, profile, coreBit, 0};
        core = createContext(dc, nullptr, attributes);
        require(core != nullptr && wglMakeCurrent(dc, core), "WGL core 4.3 context unavailable");
    }
};

class NativeContext
{
    std::optional<Window> window;
    std::optional<AngleContext> es;

public:
    void open(const AngleDisplay* angle)
    {
        if (angle)
            es.emplace(*angle).open();
        else
        {
            window.emplace().open();
            window->upgrade();
        }
    }
};

struct Gl
{
    const AngleDisplay* angle;

    explicit Gl(const AngleDisplay* angle) : angle(angle) {}

    template<class Function>
    Function bind(const char* name) const
    {
        if (!angle)
            return bindWgl<Function>(name);
        const auto address = angle->find(name);
        if (!address)
            throw std::runtime_error(std::string("OpenGL ES function unavailable: ") + name);
        return std::bit_cast<Function>(address);
    }

    GlFunction<const GLubyte*, GLenum> getString = angle ? bind<decltype(getString)>("glGetString") : glGetString;
    GlFunction<GLenum> getError = angle ? bind<decltype(getError)>("glGetError") : glGetError;
    GlFunction<void, GLenum, GLint*> getInteger = angle ? bind<decltype(getInteger)>("glGetIntegerv") : glGetIntegerv;
    GlFunction<void, GLenum, GLuint, GLint*> getIndexedInteger = bind<decltype(getIndexedInteger)>("glGetIntegeri_v");
    GlFunction<void, GLenum, int64_t*> getInteger64 = bind<decltype(getInteger64)>("glGetInteger64v");
    GlFunction<GLuint, GLenum> createShader = bind<decltype(createShader)>("glCreateShader");
    GlFunction<void, GLuint, GLsizei, const char* const*, const GLint*> source = bind<decltype(source)>("glShaderSource");
    GlFunction<void, GLuint> compile = bind<decltype(compile)>("glCompileShader");
    GlFunction<void, GLuint> deleteShader = bind<decltype(deleteShader)>("glDeleteShader");
    GlFunction<void, GLuint, GLenum, GLint*> shaderParameter = bind<decltype(shaderParameter)>("glGetShaderiv");
    GlFunction<void, GLuint, GLsizei, GLsizei*, char*> shaderLog = bind<decltype(shaderLog)>("glGetShaderInfoLog");
    GlFunction<GLuint> createProgram = bind<decltype(createProgram)>("glCreateProgram");
    GlFunction<void, GLuint, GLuint> attach = bind<decltype(attach)>("glAttachShader");
    GlFunction<void, GLuint> link = bind<decltype(link)>("glLinkProgram");
    GlFunction<void, GLuint, GLenum, GLint*> programParameter = bind<decltype(programParameter)>("glGetProgramiv");
    GlFunction<void, GLuint, GLsizei, GLsizei*, char*> programLog = bind<decltype(programLog)>("glGetProgramInfoLog");
    GlFunction<void, GLuint> deleteProgram = bind<decltype(deleteProgram)>("glDeleteProgram");
    GlFunction<void, GLuint> use = bind<decltype(use)>("glUseProgram");
    GlFunction<void, GLuint, GLenum, GLenum, GLint*> interfaceParameter =
        bind<decltype(interfaceParameter)>("glGetProgramInterfaceiv");
    GlFunction<void, GLuint, GLenum, GLuint, GLsizei, const GLenum*, GLsizei, GLsizei*, GLint*> resourceParameter =
        bind<decltype(resourceParameter)>("glGetProgramResourceiv");
    GlFunction<void, GLsizei, GLuint*> genBuffers = bind<decltype(genBuffers)>("glGenBuffers");
    GlFunction<void, GLsizei, const GLuint*> deleteBuffers = bind<decltype(deleteBuffers)>("glDeleteBuffers");
    GlFunction<void, GLenum, GLuint> bindBuffer = bind<decltype(bindBuffer)>("glBindBuffer");
    GlFunction<void, GLenum, GLuint, GLuint> bindBufferBase = bind<decltype(bindBufferBase)>("glBindBufferBase");
    GlFunction<void, GLenum, GLuint, GLuint, BufferSize, BufferSize> bindBufferRange =
        angle ? bind<decltype(bindBufferRange)>("glBindBufferRange") : nullptr;
    GlFunction<void, GLenum, BufferSize, const void*, GLenum> upload = bind<decltype(upload)>("glBufferData");
    GlFunction<void, GLenum, BufferSize, BufferSize, const void*> overwrite = bind<decltype(overwrite)>("glBufferSubData");
    GlFunction<void, GLenum, BufferSize, BufferSize, void*> read = angle ? nullptr : bind<decltype(read)>("glGetBufferSubData");
    GlFunction<void*, GLenum, BufferSize, BufferSize, GLbitfield> map = angle ? bind<decltype(map)>("glMapBufferRange") : nullptr;
    GlFunction<GLboolean, GLenum> unmap = angle ? bind<decltype(unmap)>("glUnmapBuffer") : nullptr;
    GlFunction<void, GLuint, GLuint, GLuint> dispatch = bind<decltype(dispatch)>("glDispatchCompute");
    GlFunction<void, GLbitfield> barrier = bind<decltype(barrier)>("glMemoryBarrier");
    GlFunction<Sync, GLenum, GLbitfield> fence = bind<decltype(fence)>("glFenceSync");
    GlFunction<GLenum, Sync, GLbitfield, uint64_t> wait = bind<decltype(wait)>("glClientWaitSync");
    GlFunction<void, Sync> deleteSync = bind<decltype(deleteSync)>("glDeleteSync");

    std::string_view text(GLenum property) const
    {
        const auto* value = getString(property);
        return value ? reinterpret_cast<const char*>(value) : "unavailable";
    }

    void check(const char* operation) const
    {
        const auto error = getError();
        if (error != GL_NO_ERROR)
        {
            char text[160];
            std::snprintf(text, sizeof(text), "%s failed (OpenGL error 0x%04x)", operation, error);
            throw std::runtime_error(text);
        }
    }

    void readStorage(BufferSize length, uint32_t* destination) const
    {
        if (angle)
        {
            constexpr GLbitfield mapReadBit = 0x0001;
            const auto* data = map(shaderStorageBuffer, 0, length, mapReadBit);
            check("OpenGL ES glMapBufferRange");
            require(data != nullptr, "OpenGL ES glMapBufferRange returned null");
            std::memcpy(destination, data, static_cast<size_t>(length));
            require(unmap(shaderStorageBuffer) == GL_TRUE, "OpenGL ES storage buffer unmap failed");
        }
        else
            read(shaderStorageBuffer, 0, length, destination);
        check("OpenGL readback");
    }
};

GLuint build(const Gl& gl, std::string_view source)
{
    const auto version = gl.angle ? std::string_view("#version 310 es") : std::string_view("#version 430");
    require(source.starts_with(version) && source.find('\0') == std::string_view::npos,
        gl.angle ? "expected GLSL ES 310 source" : "expected desktop GLSL 430 source");
    const auto shader = gl.createShader(computeShader);
    GLuint program = 0;
    try
    {
        const char* text = source.data();
        const auto length = static_cast<GLint>(source.size());
        gl.source(shader, 1, &text, &length);
        gl.compile(shader);
        GLint compiled = 0, logLength = 0;
        gl.shaderParameter(shader, compileStatus, &compiled);
        if (!compiled)
        {
            gl.shaderParameter(shader, infoLogLength, &logLength);
            std::string log(static_cast<size_t>(std::clamp(logLength, 1, 2048)), '\0');
            gl.shaderLog(shader, static_cast<GLsizei>(log.size()), nullptr, log.data());
            throw std::runtime_error(std::string("OpenGL compute compilation failed: ") + log.c_str());
        }
        program = gl.createProgram();
        gl.attach(program, shader);
        gl.link(program);
        GLint linked = 0;
        gl.programParameter(program, linkStatus, &linked);
        if (!linked)
        {
            gl.programParameter(program, infoLogLength, &logLength);
            std::string log(static_cast<size_t>(std::clamp(logLength, 1, 2048)), '\0');
            gl.programLog(program, static_cast<GLsizei>(log.size()), nullptr, log.data());
            throw std::runtime_error(std::string("OpenGL compute link failed: ") + log.c_str());
        }
        gl.check("compile and link");
    }
    catch (...)
    {
        gl.deleteShader(shader);
        if (program)
            gl.deleteProgram(program);
        throw;
    }
    gl.deleteShader(shader);
    return program;
}

void checkProgram(const Gl& gl, GLuint program, uint32_t workgroupX, const std::array<GLuint, 3>& locations)
{
    std::array<GLint, 3> sizes{};
    gl.programParameter(program, computeWorkgroupSize, sizes.data());
    require(sizes == std::array<GLint, 3>{static_cast<GLint>(workgroupX), 1, 1}, "unexpected OpenGL compute group size");
    GLint blocks = 0;
    gl.interfaceParameter(program, shaderStorageBlock, activeResources, &blocks);
    require(blocks == 3, "expected precisely three active storage blocks");

    std::array<bool, 3> seen{};
    for (GLuint index = 0; index < 3; ++index)
    {
        constexpr GLenum blockProperties[]{bufferBinding, numActiveVariables};
        std::array<GLint, 2> block{};
        gl.resourceParameter(program, shaderStorageBlock, index, 2, blockProperties, 2, nullptr, block.data());
        require(block[0] >= 0 && block[1] == 1, "expected one array per storage block");
        const auto location = std::find(locations.begin(), locations.end(), static_cast<GLuint>(block[0]));
        require(location != locations.end() && !seen[location - locations.begin()], "unexpected or duplicate SSBO binding");
        seen[location - locations.begin()] = true;

        GLint variable = -1;
        gl.resourceParameter(program, shaderStorageBlock, index, 1, &activeVariables, 1, nullptr, &variable);
        require(variable >= 0, "missing storage block variable");
        constexpr GLenum properties[]{variableType, variableOffset, arrayStride};
        std::array<GLint, 3> words{};
        gl.resourceParameter(program, bufferVariable, static_cast<GLuint>(variable), 3, properties, 3, nullptr, words.data());
        require(words == std::array<GLint, 3>{GL_UNSIGNED_INT, 0, 4}, "expected std430 uint array with four-byte stride");
    }
    gl.check("program reflection");
}

template<size_t Capacity>
void copyText(char (&destination)[Capacity], std::string_view source)
{
    auto length = std::min(source.size(), Capacity - 1);
    while (length > 0 && length < source.size() && (static_cast<unsigned char>(source[length]) & 0xc0u) == 0x80u)
        --length;
    std::memcpy(destination, source.data(), length);
    destination[length] = '\0';
}

GaOpenGlDevice deviceInfo(const Gl& gl)
{
    const auto vendor = gl.text(GL_VENDOR), renderer = gl.text(GL_RENDERER), version = gl.text(GL_VERSION);
    std::string identity(vendor);
    identity += ' ';
    identity += renderer;
    for (auto& ch : identity)
        ch = static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
    for (const auto* software : {"gdi generic", "llvmpipe", "softpipe", "swiftshader", "software rasterizer",
        "microsoft basic render", "warp", "null renderer"})
        require(identity.find(software) == std::string::npos, "OpenGL software renderer is unsupported");

    GLint major = 0, minor = 0, groups = 0, groupSize = 0, invocations = 0, bindings = 0, blocks = 0;
    int64_t bufferBytes = 0;
    gl.getInteger(contextMajor, &major);
    gl.getInteger(contextMinor, &minor);
    gl.getIndexedInteger(maxGroups, 0, &groups);
    gl.getIndexedInteger(maxGroupSize, 0, &groupSize);
    gl.getInteger(maxInvocations, &invocations);
    gl.getInteger(maxSsboBindings, &bindings);
    gl.getInteger(maxComputeSsboBlocks, &blocks);
    gl.getInteger64(maxSsboBytes, &bufferBytes);
    gl.check("OpenGL device limits");
    const bool versionSupported = gl.angle ? version.starts_with("OpenGL ES ") && (major > 3 || (major == 3 && minor >= 1))
        : major > 4 || (major == 4 && minor >= 3);
    require(versionSupported && groups > 0 && groupSize >= 64 && invocations >= 64
        && bindings > 7 && blocks >= 3 && bufferBytes >= static_cast<int64_t>(sizeof(uint32_t)),
        "OpenGL compute limits are insufficient");

    constexpr auto maxBytes = static_cast<int64_t>(std::numeric_limits<uint32_t>::max() & ~3u);
    GaOpenGlDevice info{static_cast<uint32_t>(major), static_cast<uint32_t>(minor), static_cast<uint32_t>(groups),
        static_cast<uint32_t>(groupSize), static_cast<uint32_t>(invocations),
        static_cast<uint32_t>(std::min(bufferBytes, maxBytes)), static_cast<uint32_t>(bindings), {}, {}, {}};
    copyText(info.vendor, vendor);
    copyText(info.renderer, renderer);
    copyText(info.version, version);
    return info;
}

void waitForReadback(const Gl& gl)
{
    const auto sync = gl.fence(syncGpuCommandsComplete, 0);
    require(sync != nullptr, "OpenGL glFenceSync returned null");
    const auto deadline = Clock::now() + std::chrono::seconds(30);
    GLenum result;
    do
    {
        result = gl.wait(sync, syncFlushCommands, 10000000u);
    } while (result == timeoutExpired && Clock::now() < deadline);
    gl.deleteSync(sync);
    require(result == alreadySignaled || result == conditionSatisfied, "OpenGL fence failed or timed out");
}

using Locations = std::array<GLuint, 3>;

struct Dispatch
{
    uint32_t groups;
    std::span<const uint32_t> input;
    std::span<uint32_t> output;
    std::span<uint32_t> faults;
};

class Pipeline
{
    const Gl& gl;
    GLuint program;
    Locations locations;
    std::array<GLuint, 3> buffers{};
    std::array<BufferSize, 3> bufferSizes{};
    std::array<std::vector<uint32_t>, 2> readback;
    bool unavailable = false;

    void release()
    {
        gl.use(0);
        gl.deleteBuffers(static_cast<GLsizei>(buffers.size()), buffers.data());
        gl.deleteProgram(program);
    }

public:
    Pipeline(const Gl& gl, std::string_view source, uint32_t workgroupX, Locations map)
        : gl(gl), program(build(gl, source)), locations(map)
    {
        try
        {
            checkProgram(gl, program, workgroupX, locations);
            gl.genBuffers(static_cast<GLsizei>(buffers.size()), buffers.data());
            gl.check("OpenGL create compute buffers");
        }
        catch (...)
        {
            release();
            throw;
        }
    }

    ~Pipeline() { release(); }

    void execute(const Dispatch& job)
    {
        require(!unavailable, "OpenGL kernel has a failed dispatch");
        readback[0].resize(job.output.size());
        readback[1].resize(job.faults.size());
        unavailable = true;
        gl.use(program);
        const std::array<std::span<const uint32_t>, 3> payload{job.input, job.output, job.faults};
        for (size_t i = 0; i < buffers.size(); ++i)
        {
            const auto length = static_cast<BufferSize>(payload[i].size_bytes());
            gl.bindBuffer(shaderStorageBuffer, buffers[i]);
            if (length == bufferSizes[i])
                gl.overwrite(shaderStorageBuffer, 0, length, payload[i].data());
            else
            {
                gl.upload(shaderStorageBuffer, length, payload[i].data(), dynamicCopy);
                bufferSizes[i] = length;
            }
            if (gl.angle)
                // ANGLE D3D11 needs the indexed size to refresh its UAV view on a shrink.
                gl.bindBufferRange(shaderStorageBuffer, locations[i], buffers[i], 0, length);
            else
                gl.bindBufferBase(shaderStorageBuffer, locations[i], buffers[i]);
        }
        gl.check("OpenGL upload and bind");
        gl.dispatch(job.groups, 1, 1);
        gl.barrier(bufferUpdateBarrier);
        gl.check("OpenGL dispatch and buffer barrier");
        waitForReadback(gl);

        for (size_t i = 0; i < readback.size(); ++i)
        {
            gl.bindBuffer(shaderStorageBuffer, buffers[i + 1]);
            gl.readStorage(bufferSizes[i + 1], readback[i].data());
        }
        std::copy(readback[0].begin(), readback[0].end(), job.output.begin());
        std::copy(readback[1].begin(), readback[1].end(), job.faults.begin());
        unavailable = false;
    }
};

class ContextWorker
{
    struct Job
    {
        void* work;
        void (*execute)(void*, const Gl&);
        std::exception_ptr failure;
        bool done = false;
    };

    GaOpenGlDevice device{};
    std::mutex mutex;
    std::condition_variable wake;
    std::thread worker;
    Job* pending = nullptr;
    bool stopping = false;

    void run(const std::filesystem::path& directory, uint32_t renderer, std::promise<GaOpenGlDevice> ready)
    {
        // Native contexts and their surfaces stay on the same worker thread.
        std::optional<AngleDisplay> angle;
        NativeContext context;
        std::optional<Gl> gl;
        try
        {
            if (!directory.empty())
                angle.emplace(directory, renderer);
            const auto* display = angle ? &*angle : nullptr;
            context.open(display);
            gl.emplace(display);
            ready.set_value(deviceInfo(*gl));
        }
        catch (...)
        {
            ready.set_exception(std::current_exception());
            return;
        }

        std::unique_lock lock(mutex);
        for (;;)
        {
            wake.wait(lock, [this] { return stopping || pending != nullptr; });
            if (stopping)
                return;
            auto& job = *pending;
            lock.unlock();
            try
            {
                job.execute(job.work, *gl);
            }
            catch (...)
            {
                job.failure = std::current_exception();
            }
            lock.lock();
            pending = nullptr;
            job.done = true;
            wake.notify_all();
        }
    }

public:
    ContextWorker(std::filesystem::path directory = {}, uint32_t renderer = 0)
    {
        std::promise<GaOpenGlDevice> ready;
        auto initialized = ready.get_future();
        worker = std::thread(&ContextWorker::run, this, std::move(directory), renderer, std::move(ready));
        try
        {
            device = initialized.get();
        }
        catch (...)
        {
            worker.join();
            throw;
        }
    }

    ~ContextWorker()
    {
        {
            std::lock_guard lock(mutex);
            stopping = true;
        }
        wake.notify_all();
        worker.join();
    }

    const GaOpenGlDevice& info() const { return device; }

    template<class Work>
    void invoke(Work work)
    {
        Job job{&work, [](void* value, const Gl& gl) { (*static_cast<Work*>(value))(gl); }, {}};
        std::unique_lock lock(mutex);
        wake.wait(lock, [this] { return pending == nullptr; });
        pending = &job;
        wake.notify_all();
        wake.wait(lock, [&job] { return job.done; });
        if (job.failure)
            std::rethrow_exception(job.failure);
    }
};

class AngleLease
{
    struct Registry
    {
        std::mutex mutex;
        std::filesystem::path directory;
        std::array<std::weak_ptr<ContextWorker>, 3> workers;
    };

    static Registry& registry()
    {
        static Registry instance;
        return instance;
    }

    std::shared_ptr<ContextWorker> worker;

public:
    AngleLease(const std::filesystem::path& directory, uint32_t renderer)
    {
        auto& live = registry();
        std::lock_guard lock(live.mutex);
        if (std::ranges::any_of(live.workers, [](const auto& entry) { return !entry.expired(); }))
        {
            std::error_code error;
            require(std::filesystem::equivalent(live.directory, directory, error),
                "A different ANGLE DLL directory is already in use");
        }
        else
            live.directory = directory;

        auto& slot = live.workers[renderer];
        worker = slot.lock();
        if (!worker)
        {
            // ANGLE backends can share one display internally. Keep context and
            // pipeline operations on one worker across all handles for this renderer.
            worker = std::make_shared<ContextWorker>(directory, renderer);
            slot = worker;
        }
    }

    ~AngleLease()
    {
        std::lock_guard lock(registry().mutex);
        worker.reset();
    }

    ContextWorker& get() const { return *worker; }
};

struct DeviceContext
{
    GaOpenGlDevice device;
    std::shared_ptr<const AngleLease> angle;
};

class Kernel
{
    DeviceContext owner;
    std::unique_ptr<ContextWorker> wgl;
    ContextWorker& worker;
    std::optional<Pipeline> pipeline;

public:
    Kernel(DeviceContext context, std::string_view source, uint32_t workgroupX, Locations bindings)
        : owner(std::move(context)), wgl(owner.angle ? nullptr : std::make_unique<ContextWorker>()),
          worker(owner.angle ? owner.angle->get() : *wgl)
    {
        const auto& actual = worker.info();
        const auto& device = owner.device;
        require(std::strcmp(actual.vendor, device.vendor) == 0 && std::strcmp(actual.renderer, device.renderer) == 0
            && std::strcmp(actual.version, device.version) == 0, "OpenGL kernel renderer changed after enumeration");
        worker.invoke([&](const Gl& gl) { pipeline.emplace(gl, source, workgroupX, bindings); });
    }

    ~Kernel() { worker.invoke([this](const Gl&) { pipeline.reset(); }); }

    void dispatch(uint32_t groups, std::span<const uint32_t> input, std::span<uint32_t> output,
        std::span<uint32_t> faults)
    {
        const auto& device = owner.device;
        require(groups > 0 && groups <= device.maxGroupsX, "OpenGL dispatch group count is out of range");
        require(input.size_bytes() <= device.maxStorageBufferRange && output.size_bytes() <= device.maxStorageBufferRange
            && faults.size_bytes() <= device.maxStorageBufferRange, "OpenGL storage buffer exceeds device limits");
        worker.invoke([&](const Gl&) { pipeline->execute({groups, input, output, faults}); });
    }
};

void probeEsBufferSizes(const DeviceContext& owner)
{
    constexpr std::string_view source = R"GLSL(#version 310 es
layout(local_size_x = 1) in;
layout(std430, binding = 3) readonly buffer InputBuffer { uint inputWords[]; };
layout(std430, binding = 5) buffer OutputBuffer { uint outputWords[]; };
layout(std430, binding = 7) buffer FaultBuffer { uint faultWords[]; };
void main()
{
    outputWords[0] = uint(inputWords.length());
    outputWords[1] = inputWords[0];
    faultWords[0] = uint(outputWords.length());
    faultWords[1] = uint(faultWords.length());
}
)GLSL";
    Kernel kernel(owner, source, 1, {3, 5, 7});
    constexpr uint32_t sentinel = 0xBADC0DEu;
    constexpr std::array<uint32_t, 3> sizes[]{{65, 64, 16}, {3, 4, 5}, {33, 24, 17}};
    for (const auto& size : sizes)
    {
        std::vector<uint32_t> input(size[0]), output(size[1], sentinel), faults(size[2], sentinel);
        input[0] = size[0] ^ sentinel;
        kernel.dispatch(1, input, output, faults);
        require(output[0] == size[0] && output[1] == input[0] && faults[0] == size[1] && faults[1] == size[2],
            "OpenGL ES storage lengths changed after buffer resize");
        require(std::all_of(output.begin() + 2, output.end(), [](auto word) { return word == sentinel; })
            && std::all_of(faults.begin() + 2, faults.end(), [](auto word) { return word == sentinel; }),
            "OpenGL ES storage length probe overwrote guard words");
    }
}

const DeviceContext& getDevice(void* context, uint32_t index)
{
    require(context != nullptr && index == 0, "OpenGL context or device index is invalid");
    return *static_cast<DeviceContext*>(context);
}

DeviceContext* openWglDevice()
{
    NativeContext context;
    context.open(nullptr);
    return new DeviceContext{deviceInfo(Gl(nullptr)), {}};
}

void writeError(char* error, uint32_t capacity, const char* text)
{
    if (!error || !capacity)
        return;
    auto length = std::min(static_cast<size_t>(capacity - 1), std::strlen(text));
    while (length > 0 && (static_cast<unsigned char>(text[length]) & 0xc0u) == 0x80u)
        --length;
    std::memcpy(error, text, length);
    error[length] = '\0';
}

template<class Work>
int32_t perform(char* error, uint32_t errorCapacity, Work work) noexcept
{
    writeError(error, errorCapacity, "");
    try
    {
        work();
        return 0;
    }
    catch (const std::exception& failure)
    {
        writeError(error, errorCapacity, failure.what());
    }
    catch (...)
    {
        writeError(error, errorCapacity, "unrecognized native OpenGL failure");
    }
    return 1;
}
}

int32_t skecs_ga_gl_open(void** context, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context != nullptr, "OpenGL context destination is required");
        *context = nullptr;
        *context = openWglDevice();
    });
}

int32_t skecs_ga_gl_open_es(const wchar_t* angleDirectory, uint32_t renderer,
    void** context, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context != nullptr, "OpenGL ES context destination is required");
        *context = nullptr;
        require(angleDirectory && renderer <= 2, "OpenGL ES ANGLE directory and renderer are required");
        const std::filesystem::path directory(angleDirectory);
        require(directory.is_absolute(), "OpenGL ES ANGLE DLL directory must be absolute");
        auto angle = std::make_shared<AngleLease>(directory, renderer);
        *context = new DeviceContext{angle->get().info(), std::move(angle)};
    });
}

void skecs_ga_gl_close(void* context)
{
    delete static_cast<DeviceContext*>(context);
}

int32_t skecs_ga_gl_device_count(void* context, uint32_t* count, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context && count, "OpenGL context and device count destination are required");
        *count = 1;
    });
}

int32_t skecs_ga_gl_get_device(void* context, uint32_t index, GaOpenGlDevice* device, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(device != nullptr, "OpenGL device destination is required");
        *device = getDevice(context, index).device;
    });
}

int32_t skecs_ga_gl_probe(void* context, uint32_t index, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        constexpr uint32_t sentinel = 0xBADC0DEu;
        constexpr std::array values{-7, std::numeric_limits<int32_t>::min(), 7, std::numeric_limits<int32_t>::max()};
        std::array<uint32_t, values.size() + 1> input{static_cast<uint32_t>(values.size())};
        std::array<uint32_t, values.size() + 1> quotients{}, remainders{};
        quotients.fill(sentinel);
        remainders.fill(sentinel);
        for (size_t i = 0; i < values.size(); ++i)
            input[i + 1] = std::bit_cast<uint32_t>(values[i]);
        const auto& owner = getDevice(context, index);
        const auto source = owner.angle ? std::string_view(startupGlslEs) : std::string_view(startupGlsl);
        Kernel kernel(owner, source, 64, {3, 5, 7});
        kernel.dispatch(1, input, quotients, remainders);
        for (size_t i = 0; i < values.size(); ++i)
            require(quotients[i] == std::bit_cast<uint32_t>(values[i] / 3)
                && remainders[i] == std::bit_cast<uint32_t>(values[i] % 3), "OpenGL startup arithmetic mismatch");
        require(quotients.back() == sentinel && remainders.back() == sentinel, "OpenGL startup overwrote guard words");
        if (owner.angle)
            probeEsBufferSizes(owner);
    });
}

int32_t skecs_ga_gl_create_kernel(void* context, uint32_t index, const char* source, uint32_t sourceByteCount,
    uint32_t workgroupX, const uint32_t* bindings, void** kernel, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel != nullptr, "OpenGL kernel destination is required");
        *kernel = nullptr;
        const auto& owner = getDevice(context, index);
        const auto& device = owner.device;
        require(source && sourceByteCount > 0 && sourceByteCount <= static_cast<uint32_t>(std::numeric_limits<GLint>::max())
            && bindings && workgroupX > 0 && workgroupX <= device.maxWorkgroupX && workgroupX <= device.maxWorkgroupInvocations,
            "OpenGL source, group size and storage bindings are required");
        const Locations map{bindings[0], bindings[1], bindings[2]};
        require(map[0] != map[1] && map[0] != map[2] && map[1] != map[2]
            && *std::max_element(map.begin(), map.end()) < device.maxStorageBindings,
            "OpenGL shader storage bindings are out of range or repeated");
        *kernel = new Kernel(owner, {source, sourceByteCount}, workgroupX, map);
    });
}

void skecs_ga_gl_release_kernel(void* kernel)
{
    delete static_cast<Kernel*>(kernel);
}

int32_t skecs_ga_gl_dispatch(void* kernel, uint32_t groupsX, const uint32_t* input, uint32_t inputLength,
    uint32_t* output, uint32_t outputLength, uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel && input && output && faults && inputLength && outputLength && faultsLength,
            "OpenGL dispatch requires a kernel and three nonempty buffers");
        static_cast<Kernel*>(kernel)->dispatch(groupsX, {input, inputLength}, {output, outputLength}, {faults, faultsLength});
    });
}
