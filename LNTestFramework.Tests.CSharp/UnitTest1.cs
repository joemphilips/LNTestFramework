using System;
using Xunit;
using LNTestFramework;
using NBitcoin;

namespace LNTestFramework.Tests.CSharp
{
    public class UnitTest1
    {
        [Fact]
        public void ShouldStartNodesAndPrepare()
        {
            using (var builder = LightningNodeLauncher.lnLauncher.createBuilder("Test1"))
            {
                builder.startNode();
                builder.ConnectAll();
                var clients = builder.GetClients();
                builder.PrepareFunds(Money.Satoshis(1000000m));
                builder.OpenChannel(clients.Rebalancer, clients.ThirdParty, Money.Satoshis(500000m));
            }
        }
    }
}
