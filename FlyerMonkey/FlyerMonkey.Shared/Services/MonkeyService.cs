using FlyerMonkey.Shared.Model;
using System.Diagnostics;
using System.Net.Http.Json;


namespace FlyerMonkey.Shared.Services
{
    public class MonkeyService
    {
        private List<Monkey> monkeysList = new();

        private readonly HttpClient httpClient;

        public MonkeyService()
        {
            httpClient = new HttpClient();
        }
        public async Task<List<Monkey>> GetMonkeys()
        {
            if (monkeysList.Count > 0)
            {
                return monkeysList;
            }
            string cacheBuster = DateTime.Now.Ticks.ToString();

            int userAge = 30;
            string userName = "Jane Doe ****************************";

            Debug.WriteLine($"User Name: {userName}, Age: {userAge}");

            System.Diagnostics.Debug.WriteLine(cacheBuster);
            var response = await httpClient.GetAsync("https://richardberriman.com/monkeys.json");
            if (response.IsSuccessStatusCode)
            {
                var monkeysResult = await response.Content.ReadFromJsonAsync(MonkeyContext.Default.ListMonkey);

                if (monkeysResult is not null)
                    monkeysList = monkeysResult;
            }
            return monkeysList;
        }

    }
}
