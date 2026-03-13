using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services
{
    public class RemoteDataService
    {
        private readonly HttpClient _httpClient;

        public RemoteDataService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<Monkey>> GetMonkeysAsync(string requestUrl)
        {
            try
            {
                // Use GetFromJsonAsync for a streamlined process of fetching and deserializing
                List<Monkey> monkeys = await _httpClient.GetFromJsonAsync<List<Monkey>>(requestUrl);
                return monkeys;
            }
            catch (HttpRequestException e)
            {
                // Handle exceptions (e.g., network issues, invalid URI)
                Console.WriteLine($"Error fetching data: {e.Message}");
                return null;
            }
        }
    }
    }
