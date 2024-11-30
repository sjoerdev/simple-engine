using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using System.Drawing;
using System.Timers;

using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

using Hexa.NET.OpenAL;

namespace Engine;

public static class Windowing
{
    private static IWindow window;

    public static string Title
    {
        get => window.Title;
        set => window.Title = value;
    }

    public static Vector2 Resolution
    {
        get => (Vector2)window.Size;
        set => window.Size = new((int)value.X, (int)value.Y);
    }

    public static bool Vsync
    {
        get => window.VSync;
        set => window.VSync = value;
    }

    public static bool Resizable
    {
        get => window.WindowBorder == WindowBorder.Resizable;
        set => window.WindowBorder = value ? WindowBorder.Resizable : WindowBorder.Fixed;
    }

    public static bool Fullscreen
    {
        get => window.WindowState == WindowState.Fullscreen;
        set => window.WindowState = value ? WindowState.Fullscreen : WindowState.Normal;
    }

    public unsafe static void CreateWindow(int width, int height, string title, Action OnLoad, Action<float> OnUpdate, Action<float> OnRender)
    {
        // create window
        var options = WindowOptions.Default;
        options.Size = new(width, height);
        options.Title = title;
        window = Window.Create(options);

        // engine initialize callbacks
        window.Load += () => Input.Initialize(window);
        window.Load += () => Drawing.Initialize(window);
        window.Load += () => 
        {
            ALCdevice* device = OpenAL.OpenDevice((byte*)null);
            ALCcontext* context = OpenAL.CreateContext(device);
            OpenAL.MakeContextCurrent(context);
        };

        // engine other callbacks
        window.Update += (double delta) => Input.UpdateInputState();
        window.FramebufferResize += (size) => Drawing.ResizeViewport(new(size.X, size.Y));

        // game callbacks
        window.Load += OnLoad;
        window.Update += (double delta) => OnUpdate((float)delta);
        window.Render += (double delta) => OnRender((float)delta);

        // run window
        window.Run();
        window.Dispose();
    }

    public static void SwapBuffers() => window.SwapBuffers();
}

public static class Input
{
    private static IInputContext inputContext;
    private static List<Key> keysPressed = [];
    private static List<Key> keysDown = [];
    private static List<Key> keysUp = [];
    private static List<MouseButton> mouseButtonsPressed = [];
    private static List<MouseButton> mouseButtonsDown = [];
    private static List<MouseButton> mouseButtonsUp = [];
    private static List<Key> keysDownLastFrame = [];
    private static List<Key> keysUpLastFrame = [];
    private static List<MouseButton> mouseButtonsDownLastFrame = [];
    private static List<MouseButton> mouseButtonsUpLastFrame = [];

    public static bool GetKey(Key key) => keysPressed.Contains(key);
    public static bool GetKeyDown(Key key) => keysDown.Contains(key);
    public static bool GetKeyUp(Key key) => keysUp.Contains(key);
    public static bool GetMouseButton(MouseButton button) => mouseButtonsPressed.Contains(button);
    public static bool GetMouseButtonDown(MouseButton button) => mouseButtonsDown.Contains(button);
    public static bool GetMouseButtonUp(MouseButton button) => mouseButtonsUp.Contains(button);
    public static Vector2 GetMousePosition() => inputContext.Mice[0].Position;

    public static void Initialize(IWindow window)
    {
        inputContext = window.CreateInput();
        var keyboard = inputContext.Keyboards[0];
        var mouse = inputContext.Mice[0];
        keyboard.KeyDown += (kb, key, idk) => keysDownLastFrame.Add((Key)key);
        keyboard.KeyUp += (kb, key, idk) => keysUpLastFrame.Add((Key)key);
        mouse.MouseDown += (mouse, button) => mouseButtonsDownLastFrame.Add(button);
        mouse.MouseUp += (mouse, button) => mouseButtonsUpLastFrame.Add(button);
    }

    public static void UpdateInputState()
    {
        keysDown.Clear();
        keysUp.Clear();

        foreach (var key in keysDownLastFrame) if (!keysPressed.Contains(key))
        {
            keysDown.Add(key);
            keysPressed.Add(key);
        }

        foreach (var key in keysUpLastFrame) if (keysPressed.Contains(key))
        {
            keysUp.Add(key);
            keysPressed.Remove(key);
        }

        keysDownLastFrame.Clear();
        keysUpLastFrame.Clear();
        mouseButtonsDown.Clear();
        mouseButtonsUp.Clear();

        foreach (var button in mouseButtonsDownLastFrame) if (!mouseButtonsPressed.Contains(button))
        {
            mouseButtonsDown.Add(button);
            mouseButtonsPressed.Add(button);
        }

        foreach (var button in mouseButtonsUpLastFrame) if (mouseButtonsPressed.Contains(button))
        {
            mouseButtonsUp.Add(button);
            mouseButtonsPressed.Remove(button);
        }

        mouseButtonsDownLastFrame.Clear();
        mouseButtonsUpLastFrame.Clear();
    }
}

public static class Drawing
{
    private static GL opengl;
    private static uint program;
    private static uint vao;
    private static uint vbo;

    public static void Initialize(IWindow window)
    {
        opengl = GL.GetApi(window);
        program = CreateShaderProgram();
        vao = opengl.GenVertexArray();
        vbo = opengl.GenBuffer();
        SetProjectionMatrix(window.Size.X, window.Size.Y);
    }

    private static uint CreateShaderProgram()
    {
        string vertSource = 
        @"
            #version 330 core

            layout(location = 0) in vec2 aPos;

            uniform mat4 projection;

            void main()
            {
                gl_Position = projection * vec4(aPos, 0.0, 1.0);
            }
        ";

        string fragSource = 
        @"
            #version 330 core

            uniform vec4 color;

            out vec4 FragColor;

            void main()
            {
                FragColor = color;
            }
        ";

        uint vertShader = CreateShader(vertSource, ShaderType.VertexShader);
        uint fragShader = CreateShader(fragSource, ShaderType.FragmentShader);

        uint shaderProgram = opengl.CreateProgram();
        opengl.AttachShader(shaderProgram, vertShader);
        opengl.AttachShader(shaderProgram, fragShader);
        opengl.LinkProgram(shaderProgram);
        opengl.DeleteShader(vertShader);
        opengl.DeleteShader(fragShader);

        return shaderProgram;
    }

    private static uint CreateShader(string source, ShaderType type)
    {
        uint shader = opengl.CreateShader(type);
        opengl.ShaderSource(shader, source);
        opengl.CompileShader(shader);
        return shader;
    }

    public static void SetProjectionMatrix(int width, int height)
    {
        opengl.UseProgram(program);
        var projectionMatrix = Matrix4x4.CreateOrthographic(width, height, -1, 1);

        // convert the matrix to a float array in colum major order
        float[] projectionArray =
        [
            projectionMatrix.M11,
            projectionMatrix.M21,
            projectionMatrix.M31,
            projectionMatrix.M41,
            projectionMatrix.M12,
            projectionMatrix.M22,
            projectionMatrix.M32,
            projectionMatrix.M42,
            projectionMatrix.M13,
            projectionMatrix.M23,
            projectionMatrix.M33,
            projectionMatrix.M43,
            projectionMatrix.M14,
            projectionMatrix.M24,
            projectionMatrix.M34,
            projectionMatrix.M44,
        ];

        opengl.UniformMatrix4(opengl.GetUniformLocation(program, "projection"), 1, false, ref projectionArray[0]);
    }
    
    public static void ClearWindow()
    {
        opengl.ClearColor(currentColor.R / 255f, currentColor.G / 255f, currentColor.B / 255f, currentColor.A / 255f);
        opengl.Clear(ClearBufferMask.ColorBufferBit);
    }
    public static void ResizeViewport(Size size)
    {
        SetProjectionMatrix(size.Width, size.Height);
        opengl.Viewport(size);
    }

    private static Color currentColor = Color.Blue;
    public static void SetColor(Color color)
    {
        currentColor = color;
        opengl.UseProgram(program);
        opengl.Uniform4(opengl.GetUniformLocation(program, "color"), color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }
    public static Color GetColor() => currentColor;
    
    // basic shapes (positions are in pixel coordinates)
    public static void DrawLine(Vector2 start, Vector2 end, int width)
    {
        DrawPrimitive([start.X, start.Y, end.X, end.Y], 2, PrimitiveType.Lines);
    }

    public static void DrawRectangle(Vector2 position, Vector2 size)
    {
        float[] vertices =
        [
            position.X, position.Y,
            position.X + size.X, position.Y,
            position.X + size.X, position.Y,
            position.X + size.X, position.Y + size.Y,
            position.X + size.X, position.Y + size.Y,
            position.X, position.Y + size.Y,
            position.X, position.Y + size.Y,
            position.X, position.Y
        ];

        DrawPrimitive(vertices, 8, PrimitiveType.Lines);
    }

    public static void DrawRectangleFilled(Vector2 position, Vector2 size)
    {
        float[] vertices =
        [
            position.X, position.Y,
            position.X + size.X, position.Y,
            position.X, position.Y + size.Y,
            position.X + size.X, position.Y,
            position.X + size.X, position.Y + size.Y,
            position.X, position.Y + size.Y
        ];

        DrawPrimitive(vertices, 6, PrimitiveType.Triangles);
    }

    public static void DrawCircle(Vector2 center, float radius, int segments = 32)
    {
        var vertices = new float[segments * 2];

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            vertices[i * 2] = center.X + radius * (float)Math.Cos(angle);
            vertices[i * 2 + 1] = center.Y + radius * (float)Math.Sin(angle);
        }

        DrawPrimitive(vertices, segments, PrimitiveType.LineLoop);
    }

    public static void DrawCircleFilled(Vector2 center, float radius, int segments = 32)
    {
        var vertices = new float[(segments + 2) * 2];

        vertices[0] = center.X;
        vertices[1] = center.Y;

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            vertices[(i + 1) * 2] = center.X + radius * (float)Math.Cos(angle);
            vertices[(i + 1) * 2 + 1] = center.Y + radius * (float)Math.Sin(angle);
        }

        vertices[(segments + 1) * 2] = vertices[2];
        vertices[(segments + 1) * 2 + 1] = vertices[3];

        DrawPrimitive(vertices, segments + 2, PrimitiveType.TriangleFan);
    }

    public static void DrawEllipse(Vector2 center, Vector2 radius, int segments = 32)
    {
        var vertices = new float[segments * 2];

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            vertices[i * 2] = center.X + radius.X * (float)Math.Cos(angle);
            vertices[i * 2 + 1] = center.Y + radius.Y * (float)Math.Sin(angle);
        }

        DrawPrimitive(vertices, segments, PrimitiveType.LineLoop);
    }

    public static void DrawEllipseFilled(Vector2 center, Vector2 radius, int segments = 32)
    {
        var vertices = new float[(segments + 2) * 2];

        vertices[0] = center.X;
        vertices[1] = center.Y;

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            vertices[(i + 1) * 2] = center.X + radius.X * (float)Math.Cos(angle);
            vertices[(i + 1) * 2 + 1] = center.Y + radius.Y * (float)Math.Sin(angle);
        }

        vertices[(segments + 1) * 2] = vertices[2];
        vertices[(segments + 1) * 2 + 1] = vertices[3];

        DrawPrimitive(vertices, segments + 2, PrimitiveType.TriangleFan);
    }

    public static void DrawTriangle(Vector2 point1, Vector2 point2, Vector2 point3)
    {
        DrawPrimitive([point1.X, point1.Y, point2.X, point2.Y, point3.X, point3.Y], 3, PrimitiveType.LineLoop);
    }

    public static void DrawTriangleFilled(Vector2 point1, Vector2 point2, Vector2 point3)
    {
        DrawPrimitive([point1.X, point1.Y, point2.X, point2.Y, point3.X, point3.Y], 3, PrimitiveType.Triangles);
    }

    private unsafe static void DrawPrimitive(float[] vertices, int vertexCount, PrimitiveType primitiveType)
    {
        opengl.BindVertexArray(vao);
        opengl.BindBuffer(GLEnum.ArrayBuffer, vbo);
        fixed (void* ptr = &vertices[0]) opengl.BufferData(GLEnum.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), ptr, GLEnum.StaticDraw);
        opengl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        opengl.EnableVertexAttribArray(0);
        opengl.DrawArrays(primitiveType, 0, (uint)vertexCount);
        opengl.DisableVertexAttribArray(0);
        opengl.BindBuffer(GLEnum.ArrayBuffer, 0);
        opengl.BindVertexArray(0);
    }

    // complex stuff
    public static void DrawSprite(Sprite sprite, Vector2 position){}
    public static void DrawText(string text, Vector2 position, int size){}
}

public class Sprite
{
    public Sprite(string path)
    {
        // load image file
    }
}

public unsafe class AudioClipWav
{
    // wav file data
    int dataPosition;
    int sampleRate;
    bool stereo;
    int bitsPerSample;

    // openal
    uint[] buffers;
    uint source;

    // streaming
    Timer timer;
    FileStream stream;
    int bufferAmount = 4;
    int secondsPerBuffer = 1;
    int bufferSize => sampleRate * (stereo ? 2 : 1) * (bitsPerSample / 8) * secondsPerBuffer;

    public AudioClipWav(string path)
    {
        // start audio file stream
        stream = new FileStream(path, FileMode.Open, FileAccess.Read);

        // read wav header
        byte[] header = new byte[44];
        stream.Read(header, 0, 44);
        sampleRate = BitConverter.ToInt32(header, 24);
        stereo = BitConverter.ToInt16(header, 22) > 1;
        bitsPerSample = BitConverter.ToInt16(header, 34);

        // find and skip to actual sound data position
        dataPosition = FindDataChunkPosition();
        stream.Position = dataPosition;

        // setup openal buffers
        buffers = new uint[bufferAmount];
        fixed (uint* ptr = &buffers[0]) OpenAL.GenBuffers(bufferAmount, ptr);

        // setup openal source
        fixed (uint* ptr = &source) OpenAL.GenSources(1, ptr);
        OpenAL.SetSourceProperty(source, ALEnum.SourceType, (int)ALEnum.Streaming);

        // Setup timer
        timer = new Timer(50);
        timer.Elapsed += (sender, args) => RecycleUsedBuffers();
    }

    public void Start()
    {
        // cannot start if already playing or paused
        if (GetState() == ALEnum.Playing || GetState() == ALEnum.Paused) return;
        
        // que first buffers
        for (int i = 0; i < bufferAmount; i++)
        {
            FillBuffer(buffers[i]);
            fixed (uint* ptr = &buffers[i]) OpenAL.SourceQueueBuffers(source, 1, ptr);
        }

        // start source and timer
        OpenAL.SourcePlay(source);
        timer.Start();
    }

    public void PauseOrContinue()
    {
        RecycleUsedBuffers();
        if (GetState() == ALEnum.Playing) OpenAL.SourcePause(source);
        else if (GetState() == ALEnum.Paused) OpenAL.SourcePlay(source);
    }

    public void Stop()
    {
        // already stopped
        if (GetState() == ALEnum.Stopped) return;

        // stop timer and source
        timer.Stop();
        OpenAL.SourceStop(source);

        // unque buffers
        OpenAL.GetSourceProperty(source, ALEnum.BuffersProcessed, out int processed);
        if (processed > 0)
        {
            uint[] buffersToUnqueue = new uint[processed];
            fixed (uint* ptr = &buffersToUnqueue[0]) OpenAL.SourceUnqueueBuffers(source, processed, ptr);
        }

        // reset filestream position
        stream.Position = dataPosition;
    }

    // finds the data chunk position
    public int FindDataChunkPosition()
    {
        using var tempStream = new FileStream(stream.Name, FileMode.Open, FileAccess.Read);
        
        // skip the 12 byte long riff header ("riff", filesize, "wave")
        tempStream.Position = 12;

        while (tempStream.Position < tempStream.Length)
        {
            // read chunk name and size
            byte[] buffer = new byte[8];
            tempStream.Read(buffer, 0, 8);
            string chunkName = System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
            int chunkSize = BitConverter.ToInt32(buffer, 4);

            // if chunk name is data then we found the position
            if (chunkName == "data") return (int)tempStream.Position;

            // skip chunk
            tempStream.Position += chunkSize;
        }

        return 0;
    }

    // fills a buffer at the current filestream position
    private void FillBuffer(uint buffer)
    {
        byte[] audioData = new byte[bufferSize];
        int bytesRead = stream.Read(audioData, 0, audioData.Length);
        if (bytesRead == 0) { OpenAL.SourceStop(source); Console.WriteLine("it happened"); return; }
        fixed (void* data = &audioData[0]) OpenAL.BufferData(buffer, GetFormat(), data, bytesRead, sampleRate);
    }

    // if a buffer is used it gets recycled and requed
    private void RecycleUsedBuffers()
    {
        OpenAL.GetSourceProperty(source, ALEnum.BuffersProcessed, out int processed);
        for (uint i = 0; i < processed; i++)
        {
            uint buffer;
            OpenAL.SourceUnqueueBuffers(source, 1, &buffer);
            FillBuffer(buffer);
            OpenAL.SourceQueueBuffers(source, 1, &buffer);
        }
    }

    private ALEnum GetFormat()
    {
        ALEnum format;
        format = stereo ? (bitsPerSample == 16 ? ALEnum.FormatStereo16 : ALEnum.FormatStereo8) : (bitsPerSample == 16 ? ALEnum.FormatMono16 : ALEnum.FormatMono8);
        return format;
    }

    private ALEnum GetState()
    {
        OpenAL.GetSourceProperty(source, ALEnum.SourceState, out int state);
        return (ALEnum)state;
    }
}

public enum Key
{
    Unknown = -1,
    Space = 32,
    Apostrophe = 39,
    Comma = 44,
    Minus = 45,
    Period = 46,
    Slash = 47,
    Number0 = 48,
    D0 = Number0,
    Number1 = 49,
    Number2 = 50,
    Number3 = 51,
    Number4 = 52,
    Number5 = 53,
    Number6 = 54,
    Number7 = 55,
    Number8 = 56,
    Number9 = 57,
    Semicolon = 59,
    Equal = 61,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    LeftBracket = 91,
    BackSlash = 92,
    RightBracket = 93,
    GraveAccent = 96,
    World1 = 161,
    World2 = 162,
    Escape = 256,
    Enter = 257,
    Tab = 258,
    Backspace = 259,
    Insert = 260,
    Delete = 261,
    Right = 262,
    Left = 263,
    Down = 264,
    Up = 265,
    PageUp = 266,
    PageDown = 267,
    Home = 268,
    End = 269,
    CapsLock = 280,
    ScrollLock = 281,
    NumLock = 282,
    PrintScreen = 283,
    Pause = 284,
    F1 = 290,
    F2 = 291,
    F3 = 292,
    F4 = 293,
    F5 = 294,
    F6 = 295,
    F7 = 296,
    F8 = 297,
    F9 = 298,
    F10 = 299,
    F11 = 300,
    F12 = 301,
    F13 = 302,
    F14 = 303,
    F15 = 304,
    F16 = 305,
    F17 = 306,
    F18 = 307,
    F19 = 308,
    F20 = 309,
    F21 = 310,
    F22 = 311,
    F23 = 312,
    F24 = 313,
    F25 = 314,
    Keypad0 = 320,
    Keypad1 = 321,
    Keypad2 = 322,
    Keypad3 = 323,
    Keypad4 = 324,
    Keypad5 = 325,
    Keypad6 = 326,
    Keypad7 = 327,
    Keypad8 = 328,
    Keypad9 = 329,
    KeypadDecimal = 330,
    KeypadDivide = 331,
    KeypadMultiply = 332,
    KeypadSubtract = 333,
    KeypadAdd = 334,
    KeypadEnter = 335,
    KeypadEqual = 336,
    ShiftLeft = 340,
    ControlLeft = 341,
    AltLeft = 342,
    SuperLeft = 343,
    ShiftRight = 344,
    ControlRight = 345,
    AltRight = 346,
    SuperRight = 347,
    Menu = 348
}