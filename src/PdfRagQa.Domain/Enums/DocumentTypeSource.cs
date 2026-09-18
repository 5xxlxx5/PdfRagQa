namespace PdfRagQa.Domain;

/// <summary>
/// 文档类型的来源。
/// 接入时管理员可以显式声明类型，也可以留空由解析信号自动判定；
/// 记录来源是为了事后统计「自动判定 vs 人工声明」的差异，形成标注集。
/// </summary>
public enum DocumentTypeSource
{
    /// <summary>由轻量预解析信号自动判定（管理员未声明）。</summary>
    Auto = 0,

    /// <summary>由管理员在接入时显式声明。</summary>
    Declared = 1,
}
