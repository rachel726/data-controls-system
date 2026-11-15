namespace DataControls.Core.Catalog;

/// <summary>Supplies the current catalog snapshot (implemented in Infrastructure, typically cached).</summary>
public interface ICatalogProvider
{
    Task<MetadataCatalog> GetCatalogAsync(CancellationToken cancellationToken);
}
