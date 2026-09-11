using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Sites.Dto
{
    // Raw def facts only, never a family/skill/cost: the client reads them, the server decides what they mean.
    public class SiteOutputMetadata
    {
        [JsonProperty("def")]        public string DefName { get; set; } = "";
        [JsonProperty("label")]      public string Label   { get; set; } = "";

        [JsonProperty("cats")]       public List<string> Categories      { get; set; } = new List<string>();
        [JsonProperty("stuff")]      public List<string> StuffCategories { get; set; } = new List<string>();
        [JsonProperty("tags")]       public List<string> Tags            { get; set; } = new List<string>();

        [JsonProperty("ingestible")] public bool   IsIngestible { get; set; }
        [JsonProperty("food_type")]  public string FoodType     { get; set; } = "";

        [JsonProperty("animal")]     public bool IsAnimalProduct     { get; set; }
        [JsonProperty("harvested")]  public bool IsHarvestedFromPlant { get; set; }
        [JsonProperty("tree")]       public bool IsTreeHarvest       { get; set; }
        [JsonProperty("wild")]       public bool IsWildHarvest       { get; set; }
        [JsonProperty("mineable")]   public bool IsMineable          { get; set; }
        [JsonProperty("crafted")]    public bool IsCraftedProduct    { get; set; }

        [JsonProperty("mv")]         public float MarketValue { get; set; }
    }
}
