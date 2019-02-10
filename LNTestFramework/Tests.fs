namespace LNTestFramework

module Tests =
    open LNTestFramework.LightningNodeLauncher
    open Xunit
    open SwaggerProvider

    let [<Literal>] schema = "https://github.com/lightningnetwork/lnd/raw/master/lnrpc/rpc.swagger.json"
    type LND = SwaggerProvider<schema>

    [<Fact>]
    let ``lnLauncherDemo`` () =
        use builder = lnLauncher.createBuilder()
        builder.startNode()
        builder.ConnectAll() |> ignore
        Assert.True(true)
