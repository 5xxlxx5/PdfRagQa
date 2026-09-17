namespace PdfRagQa.Domain;

/// <summary>
/// 文档类型：决定入库切分与检索策略。
/// 使用手册 -> 混合检索 + 重排序；宣传手册 -> 多模态 RAG。
/// </summary>
public enum DocumentType
{
    /// <summary>产品使用手册（结构化说明性文档）</summary>
    Manual,

    /// <summary>产品宣传手册（海报式、图文混排、版面驱动）</summary>
    Brochure,
}