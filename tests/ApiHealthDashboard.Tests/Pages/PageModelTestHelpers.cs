using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ApiHealthDashboard.Tests.Pages;

internal static class PageModelTestHelpers
{
    public static TempDataDictionary CreateTempData()
    {
        return new TempDataDictionary(new DefaultHttpContext(), new InMemoryTempDataProvider());
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object>(_values);
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _values = new Dictionary<string, object>(values);
        }
    }
}
