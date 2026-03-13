using FlyerMonkey.Shared.Model;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

namespace FlyerMonkey.Shared.Services;
public class PricelineService
{
    private List<Priceline> pricelinesList = new();

    private readonly HttpClient httpClient;

    public PricelineService()
    {
        httpClient = new HttpClient();
    }
    public async Task<List<Priceline>> GetPriceline()
    {
        if (pricelinesList.Count > 0)
        {
            return pricelinesList;
        }

        var response = await httpClient.GetAsync("https://richardberriman.com/20260119_priceline.json");
        if (response.IsSuccessStatusCode)
        {
            var pricelinesResult = await response.Content.ReadFromJsonAsync(PricelineContext.Default.ListPriceline);

            if (pricelinesResult is not null)
                pricelinesList = pricelinesResult;
        }
        return pricelinesList;
    }

}
