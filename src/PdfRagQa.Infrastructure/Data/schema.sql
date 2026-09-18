-- PdfRagQa 数据库初始化脚本（SQL Server / LocalDB）
-- 说明：开发环境使用 LocalDB 实例 "MSSQLLocalDB"，直接运行本脚本建库建表。
-- 生产环境可在 SQL Server 上按相同脚本执行。

IF DB_ID(N'PdfRagQa') IS NULL
BEGIN
    CREATE DATABASE [PdfRagQa];
END
GO

USE [PdfRagQa];
GO

-- 文档：一份文档可存在多版本（document_id + version 唯一）；is_latest 标记检索默认命中版本
IF OBJECT_ID(N'dbo.document', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.document
    (
        document_id   VARCHAR(64)   NOT NULL,
        version       VARCHAR(32)   NOT NULL,
        title         NVARCHAR(256) NOT NULL,
        doc_type      INT           NOT NULL,         -- 0=Manual 1=Brochure (DocumentType)，实际用于路由的类型
        [language]    INT           NOT NULL,         -- 0=Zh 1=En 2=ZhEn (LanguageCode)
        source_file   NVARCHAR(512) NULL,
        is_latest     BIT           NOT NULL DEFAULT 0,
        imported_at   DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
        content_hash  CHAR(64)      NULL,             -- SHA256，用于缓存失效/查重
        type_source   VARCHAR(16)   NULL,             -- 'Auto'=自动判定 / 'Declared'=管理员声明
        declared_type INT           NULL,             -- 管理员声明的类型（NULL=未声明）
        auto_type     INT           NULL,             -- 自动判定意见（始终记录，用于与人工声明做准确率对照）
        type_reasons  NVARCHAR(512) NULL,             -- 判定依据与冲突告警
        CONSTRAINT PK_document PRIMARY KEY (document_id, version)
    );

    CREATE INDEX IX_document_islatest ON dbo.document (document_id, is_latest);
END
GO

-- 分类溯源字段：老库幂等补齐（表已存在时上面的 CREATE TABLE 不会执行）
IF COL_LENGTH(N'dbo.document', N'type_source') IS NULL
    ALTER TABLE dbo.document ADD type_source VARCHAR(16) NULL;
GO

IF COL_LENGTH(N'dbo.document', N'declared_type') IS NULL
    ALTER TABLE dbo.document ADD declared_type INT NULL;
GO

IF COL_LENGTH(N'dbo.document', N'auto_type') IS NULL
    ALTER TABLE dbo.document ADD auto_type INT NULL;
GO

IF COL_LENGTH(N'dbo.document', N'type_reasons') IS NULL
    ALTER TABLE dbo.document ADD type_reasons NVARCHAR(512) NULL;
GO

-- chunk：检索最小单元（手册=段落/表格，宣传册=版面块）；bbox 支持前端高亮溯源
IF OBJECT_ID(N'dbo.chunk', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.chunk
    (
        chunk_id             VARCHAR(64)   NOT NULL,
        document_id          VARCHAR(64)   NOT NULL,
        version              VARCHAR(32)   NOT NULL,
        [language]           INT           NOT NULL,
        page_no              INT           NOT NULL,
        bbox_x               FLOAT         NULL,
        bbox_y               FLOAT         NULL,
        bbox_w               FLOAT         NULL,
        bbox_h               FLOAT         NULL,
        section              NVARCHAR(256) NULL,
        [text]               NVARCHAR(MAX) NOT NULL,
        table_markdown       NVARCHAR(MAX) NULL,
        image_ref            NVARCHAR(512) NULL,
        image_description    NVARCHAR(MAX) NULL,
        embedding_model      NVARCHAR(128) NULL,
        dimension            INT           NULL,
        vector_json          NVARCHAR(MAX) NULL,       -- 向量占位(JSON)，后续由独立向量库/Milvus 承载
        created_at           DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
        CONSTRAINT PK_chunk PRIMARY KEY (chunk_id)
    );

    CREATE INDEX IX_chunk_doc ON dbo.chunk (document_id, version, page_no);
    CREATE INDEX IX_chunk_lang ON dbo.chunk ([language]);
END
GO

-- qa_feedback：用户点赞/点踩/人工修正（FR9 反馈闭环）
IF OBJECT_ID(N'dbo.qa_feedback', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.qa_feedback
    (
        id                VARCHAR(32)   NOT NULL,
        conversation_id   NVARCHAR(64)  NULL,
        question          NVARCHAR(MAX) NOT NULL,
        answer            NVARCHAR(MAX) NOT NULL,
        is_upvote         BIT           NULL,
        rejection_reason  NVARCHAR(512) NULL,
        corrected_answer  NVARCHAR(MAX) NULL,           -- 人工修正后回写为黄金标注
        created_at        DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
        CONSTRAINT PK_qa_feedback PRIMARY KEY (id)
    );

    CREATE INDEX IX_qa_feedback_conv ON dbo.qa_feedback (conversation_id);
END
GO