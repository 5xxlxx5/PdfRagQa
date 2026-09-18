using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Application.Services;

namespace PdfRagQa.Infrastructure.Imports;

/// <summary>
/// 单次导入作业的内存状态。后台线程写入，前端 GET 轮询读取。
/// 进程内内存态：服务重启即丢失，仅用于展示运行中的进度；最终结果仍落库，不依赖它。
/// </summary>
public sealed class ImportJob
{
    public string JobId { get; }
    public string Stage { get; private set; } = "已提交";
    public int CurrentItem { get; private set; }
    public int? TotalItems { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsFailed { get; private set; }
    public string? Error { get; private set; }
    public ImportDocumentResult? Result { get; private set; }

    public ImportJob(string jobId) => JobId = jobId;

    public ImportStatusDto ToStatus() =>
        new(JobId, IsCompleted, IsFailed, Stage, CurrentItem, TotalItems, Error, Result);

    public void ReportProgress(ImportProgressUpdate update)
    {
        lock (this)
        {
            Stage = update.Stage;
            CurrentItem = update.CurrentItem;
            TotalItems = update.TotalItems ?? TotalItems;
        }
    }

    public void Complete(ImportDocumentResult result)
    {
        lock (this) { Result = result; Stage = "完成"; IsCompleted = true; }
    }

    public void Fail(string error)
    {
        lock (this) { Error = error; Stage = "失败"; IsFailed = true; }
    }
}

/// <summary>
/// 后台导入作业调度：POST /import 立即返回 jobId，导入在后台执行，
/// 前端通过 GET /import/{id} 轮询 <see cref="ImportJob"/>。
/// 后台任务用独立 DI scope，保证 scoped 服务（仓储、DB 连接）不被请求线程回收。
/// </summary>
public sealed class ImportJobRunner(IServiceProvider services)
{
    private readonly ConcurrentDictionary<string, ImportJob> _jobs = new();

    public ImportJob Start(ImportDocumentRequest request)
    {
        var job = new ImportJob(Guid.NewGuid().ToString("N")[..12]);
        _jobs.TryAdd(job.JobId, job);

        _ = Task.Run(async () =>
        {
            using var scope = services.CreateScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<DocumentIngestionService>();
            var progress = new Progress<ImportProgressUpdate>(job.ReportProgress);

            try
            {
                var result = await ingestion.ImportAsync(request, progress, CancellationToken.None);
                job.Complete(result);
            }
            catch (Exception ex)
            {
                job.Fail(ex.Message);
            }
        });

        return job;
    }

    public ImportStatusDto? GetStatus(string id) =>
        _jobs.TryGetValue(id, out var job) ? job.ToStatus() : null;
}