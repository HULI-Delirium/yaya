using Yaya.Core.Monitoring;

namespace Yaya.Core.Storage;

/// <summary>corrections 表的一行（查询用纯数据）。</summary>
public sealed record CorrectionRow(DateTimeOffset AtUtc, UserCorrectionKind Kind);
