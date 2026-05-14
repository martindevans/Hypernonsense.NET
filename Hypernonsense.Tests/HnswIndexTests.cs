using System.Data.SQLite;
using Hypernonsense.HierarchicalNavigableSmallWorld;

namespace Hypernonsense.Tests;

[TestClass]
public class HnswIndexTests
{
    [TestMethod]
    public void InitTest()
    {
        var connection = new SQLiteConnection("Data Source=:memory:");
        connection.Open();
        
        HnswIndex.Init(connection);
    }
}