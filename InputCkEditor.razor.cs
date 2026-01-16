using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace CKEditor;

public partial class InputCkEditor : IAsyncDisposable
{
    private readonly Guid _guid = Guid.NewGuid();
    private readonly DotNetObjectReference<InputCkEditor> _reference;
    private IJSObjectReference? _jsModule;
    public const int MAX_HEIGHT = 400;

    private readonly Dictionary<string, List<byte>> _upload = [];

    public StringBuilder _sb = new();

    [Parameter]
    public bool ReadOnly { get; set; }

    public InputCkEditor()
    {
        _reference = DotNetObjectReference.Create(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _jsModule = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Blazor.CKEditor/ckeditor.interop.js?ver=2");
            await _jsModule.InvokeVoidAsync("setup", [_guid, _reference]);
            await _jsModule.InvokeVoidAsync("setReadonly", [_guid, ReadOnly]);
        }

        await base.OnAfterRenderAsync(firstRender);
    }
    [JSInvokable]
    public Task<string> UploadChunkAsync(byte[] data, long fileSize, string filename)
    {
        if (!_upload.TryGetValue(filename, out List<byte>? value))
        {
            _upload.Add(filename, [.. data]);
        }
        else
        {
            value.AddRange(data);
        }

        var upload = _upload[filename];

        if (upload.Count + 1 >= fileSize)
        {

            // Check Image
            using var image = Image.Load([.. upload]);

            // Calculate new dimensions while preserving aspect ratio
            int newWidth, newHeight;
            if (image.Height > MAX_HEIGHT)
            {
                newHeight = MAX_HEIGHT;
                newWidth = (int)((float)image.Width / image.Height * MAX_HEIGHT);
            }
            else
            {
                newWidth = image.Width;
                newHeight = image.Height;
            }

            // Resize the image
            image.Mutate(x => x.Resize(newWidth, newHeight));

            // Optimize and save the image
            var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
            {
                Quality = 95 // Adjust quality as needed
            };

            using MemoryStream stream = new();
            image.Save(stream, encoder);

            // Generate Base64
            string base64 = Convert.ToBase64String(stream.ToArray());
            return Task.FromResult($"data:image/jpeg;base64,{base64}");

        }

        return Task.FromResult(string.Empty);
    }

    [JSInvokable]
    public async Task EditorDataChanged(string data, bool isCompleted)
    {
        if (_jsModule is null)
        {
            return;
        }

        _sb.Append(data);

        if (isCompleted)
        {
            string value = _sb.ToString();
            _sb.Clear();
            Value = value;
            await ValueChanged.InvokeAsync(Value);
        }
    }

    public async Task UpdateAsync(string data)
    {
        if (_jsModule is not null)
        {
            await _jsModule.InvokeVoidAsync("update", _guid, data);
        }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_jsModule is not null)
            {
                await _jsModule.InvokeVoidAsync("destroy", _guid);
                await _jsModule.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or
                                   OperationCanceledException)
        {
            // The JSRuntime side may routinely be gone already if the reason we're disposing is that
            // the client disconnected. This is not an error.
        }
    }
}
