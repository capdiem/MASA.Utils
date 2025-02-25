namespace Masa.Utils.Data.Elasticsearch.Response.Index;

public class GetIndexByAliasResponse : ResponseBase
{
    public string[] IndexNames { get; }

    public GetIndexByAliasResponse(CatResponse<CatIndicesRecord> catResponse) : base(catResponse)
    {
        IndexNames = catResponse.IsValid ? catResponse.Records.Select(r => r.Index).ToArray() : Array.Empty<string>();
        IndexNames = catResponse.IsValid ? catResponse.Records.Select(r => r.Index).ToArray() : Array.Empty<string>();
    }
}
