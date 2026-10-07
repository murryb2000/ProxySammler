using System.Collections.Generic;

namespace ProxySammler.Core;

public sealed record CollectionResult(IReadOnlyList<ProxyEndpoint> Proxies, IReadOnlyList<SourceResult> Sources);
