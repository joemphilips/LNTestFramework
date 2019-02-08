namespace LNTestFramework
module Tests =
    open LNTestFramework.LightningNodeLauncher

    open Xunit

    [<Fact>]
    let ``lnLauncherDemo`` () =
        use builder = lnLauncher.createBuilder()
        builder.startNode()
        builder.ConnectAll() |> ignore
        Assert.True(true)
