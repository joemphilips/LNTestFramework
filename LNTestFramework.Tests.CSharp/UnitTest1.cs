using System;
using Xunit;
using LNTestFramework;
using NBitcoin;
using BTCPayServer.Lightning;

namespace LNTestFramework.Tests.CSharp
{
    public class UnitTest1
    {
        [Fact]
        public async void ShouldStartNodesAndPrepare()
        {
            using (var builder = LightningNodeLauncher.lnLauncher.createBuilder("Test1"))
            {
                builder.startNode();
                builder.ConnectAll();
                var clients = builder.GetClients();
                builder.PrepareLNFunds(Money.Coins(10m));
                builder.OpenChannel(clients.Bitcoin, clients.Rebalancer, clients.ThirdParty, Money.Satoshis(500000m));
                builder.OpenChannel(clients.Bitcoin, clients.Custody, clients.ThirdParty, Money.Satoshis(500000m));

                var destInfo = await clients.Custody.GetInfo();
                var routeResp = await clients.Rebalancer.SwaggerClient.QueryRoutesAsync(destInfo.NodeInfo.NodeId.ToHex(), "1000", 1);
                Assert.NotNull(routeResp.Routes);
                Assert.NotEmpty(routeResp.Routes);

                var destInvoice = await clients.Custody.CreateInvoice(LightMoney.Satoshis(1000), "UnitTest1", TimeSpan.FromMinutes(5.0));
            }
        }
    }
}
