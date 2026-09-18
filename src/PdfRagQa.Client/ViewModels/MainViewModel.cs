using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfRagQa.Client.Models;
using PdfRagQa.Client.Services;

namespace PdfRagQa.Client.ViewModels;

/// <summary>导入/问答/溯源的主视图模型。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ApiClient _api = new();

    private static readonly DocTypeOption[] DocTypeOptions =
    [
        new(null, "手册（文本链路）"),
        new(DocumentType.Manual, "手册（显式指定）"),
        new(DocumentType.Brochure, "宣传册（视觉链路）"),
    ];

    public IReadOnlyList<DocTypeOption> AvailableDocTypes { get; } = DocTypeOptions;

    [ObservableProperty]
    private string _apiBaseUrl = "http://localhost:5286";

    [ObservableProperty]
    private DocTypeOption _selectedDocType = DocTypeOptions[0];

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    [ObservableProperty]
    private double _importProgressValue;

    [ObservableProperty]
    private double _importProgressMaximum = 100;

    [ObservableProperty]
    private bool _isImportIndeterminate = true;

    [ObservableProperty]
    private string _importProgressText = "等待开始…";

    [ObservableProperty]
    private string _question = string.Empty;

    [ObservableProperty]
    private string _answer = string.Empty;

    [ObservableProperty]
    private string _answerMeta = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<CitationDto> Citations { get; } = [];

    private string? _conversationId;

    public MainViewModel()
    {
        _api.SetBaseUrl(ApiBaseUrl);
    }

    partial void OnApiBaseUrlChanged(string value)
    {
        try
        {
            _api.SetBaseUrl(value);
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"API 地址无效：{ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 PDF 文档",
            Filter = "PDF 文件 (*.pdf)|*.pdf|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        ErrorMessage = string.Empty;
        ImportStatus = string.Empty;
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            ErrorMessage = "请先选择要导入的 PDF 文件。";
            return;
        }

        IsBusy = true;
        IsImportIndeterminate = true;
        ImportProgressValue = 0;
        ImportProgressText = "正在提交…";
        try
        {
            var request = new ImportDocumentRequest
            {
                FilePath = FilePath.Trim(),
                Version = null,
                DocumentType = SelectedDocType.Value,
            };

            var submission = await _api.SubmitImportAsync(request);
            var jobId = submission.JobId;
            ImportProgressText = $"已提交（{jobId}），正在处理…";

            var deadline = DateTimeOffset.Now.AddMinutes(10);
            while (true)
            {
                await Task.Delay(400);
                var status = await _api.GetImportStatusAsync(jobId);
                if (status is null)
                {
                    ErrorMessage = $"作业 {jobId} 不存在或已失效。";
                    break;
                }

                if (status.TotalItems is > 0)
                {
                    IsImportIndeterminate = false;
                    ImportProgressMaximum = status.TotalItems.Value;
                }
                ImportProgressValue = status.CurrentItem;
                ImportProgressText = status.TotalItems is > 0
                    ? $"{status.Stage}：{status.CurrentItem}/{status.TotalItems}"
                    : $"{status.Stage}：{status.CurrentItem}";

                if (status.IsCompleted)
                {
                    ShowImportResult(status.Result);
                    break;
                }
                if (status.IsFailed)
                {
                    ErrorMessage = $"导入失败：{status.Error}";
                    break;
                }
                if (DateTimeOffset.Now > deadline)
                {
                    ErrorMessage = "导入超时（超过 10 分钟）。";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowImportResult(ImportDocumentResult? result)
    {
        IsImportIndeterminate = false;
        ImportProgressText = "导入完成";

        if (result is null)
        {
            ImportStatus = "导入完成（未返回结果明细）。";
            return;
        }

        ImportStatus = $"导入完成：docId={result.DocumentId}，版本={result.Version}，chunk 数={result.ChunkCount}，类型来源={result.TypeSource}。";
        if (result.Warnings.Count > 0)
        {
            ImportStatus += Environment.NewLine + "警告：" + string.Join("；", result.Warnings);
        }
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Question))
        {
            ErrorMessage = "请输入问题。";
            return;
        }

        IsBusy = true;
        try
        {
            var request = new QuestionRequest
            {
                Question = Question.Trim(),
                ConversationId = _conversationId,
                DocumentType = SelectedDocType.Value,
                Version = null,
            };
            var response = await _api.AskAsync(request);

            Answer = response.Answer;
            AnswerMeta = $"对话ID={response.ConversationId}，类型={response.DocumentType}，版本={response.Version}，置信度={response.Confidence:P0}";
            _conversationId = response.ConversationId;

            Citations.Clear();
            foreach (var c in response.Citations)
                Citations.Add(c);

            if (response.ImageRefs.Count > 0)
                AnswerMeta += $"。图片证据 {response.ImageRefs.Count} 张";
            else
                AnswerMeta += "。无图片证据";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"问答失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewSession()
    {
        _conversationId = null;
        Answer = string.Empty;
        AnswerMeta = string.Empty;
        Citations.Clear();
        Question = string.Empty;
        ErrorMessage = string.Empty;
    }

    /// <summary>文档类型下拉的选项模型。</summary>
    public sealed record DocTypeOption(DocumentType? Value, string Label);
}