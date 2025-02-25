namespace Masa.Utils.Data.Elasticsearch.Response;

public class BulkResponse : ResponseBase
{
    public List<BulkResponseItems> Items { get; set; }

    public BulkResponse(Nest.BulkResponse bulkResponse) : base(bulkResponse)
    {
        Items = bulkResponse.Items.Select(item => new BulkResponseItems(item.Id, item.IsValid, item.Error?.ToString() ?? string.Empty)).ToList();
    }
}
