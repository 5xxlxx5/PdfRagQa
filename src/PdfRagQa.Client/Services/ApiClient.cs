using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PdfRagQa.Client.Models;

namespace PdfRagQa.Client.Services;

/// <summary>
/// 调用 PdfRagQa REST API 的客户端封装。
/// 服务端（ASP.NET Core / System.Text.Json Web 默认）输出 camelCase 且属性名大小写不敏感，
/// 这里用相同选项反序列化，属性名即可直接写 PascalCase。
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;

    public ApiClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    /// <summary>设置 API 基地址（如 http://localhost:5286）。</summary>
    public void SetBaseUrl(string baseUrl)
    {
        var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            _http.BaseAddress = null;
            return;
        }
        _http.BaseAddress = new Uri(url, UriKind.Absolute);
    }

    public async Task<ImportStatusDto> SubmitImportAsync(
        ImportDocumentRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/documents/import", request, _json, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ImportStatusDto>(_json, ct)
               ?? throw new InvalidOperationException("导入提交返回空结果");
    }

    /// <summary>查询导入作业进度；作业不存在时返回 null。</summary>
    public async Task<ImportStatusDto?> GetImportStatusAsync(string jobId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/documents/import/{Uri.EscapeDataString(jobId)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ImportStatusDto>(_json, ct);
    }

    public async Task<QuestionResponse> AskAsync(
        QuestionRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/questions", request, _json, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<QuestionResponse>(_json, ct) ?? throw new InvalidOperationException("问答返回空结果");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var preview = body.Length > 400 ? body[..400] : body;
        throw new HttpRequestException(
            $"API 返回 HTTP {(int)response.StatusCode}：{preview}");
    }
}