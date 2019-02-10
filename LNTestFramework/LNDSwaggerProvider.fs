namespace LNTestFramework


// Since some methods (namely QueryRoutesAsync) in BTCPayServer.Lightning.LND.SwaggerClient does not work properly and always returns 400 error,
// here is alternative.
[<AutoOpen>]
module LNDSwaggerProvider =
    open System
    open System.Globalization
    open System.Threading
    open System.Threading.Tasks
    open FSharp.Control.Tasks.V2.ContextInsensitive
    open NBitcoin
    open SwaggerProvider
    open BTCPayServer.Lightning

    let [<Literal>] schema = "https://github.com/lightningnetwork/lnd/raw/master/lnrpc/rpc.swagger.json"
    type LNDSwaggerProvider = SwaggerProvider<schema>

    let bitString(hash: byte[]) =
        BitConverter.ToString(hash).Replace("-", "").ToLower(CultureInfo.InvariantCulture)

    let convertLndInvoice (resp: LNDSwaggerProvider.lnrpcInvoice): LightningInvoice =
        let result = new LightningInvoice()
        result.Id <- bitString(resp.RHash)
        result.Amount <- LightMoney.Satoshis(Convert.ToInt64(resp.Value, CultureInfo.InvariantCulture))
        result.BOLT11 <- resp.PaymentRequest
        result.Status <- LightningInvoiceStatus.Unpaid
        let fromNow = Convert.ToInt64(resp.CreationDate, CultureInfo.InvariantCulture) - Convert.ToInt64(resp.Expiry, CultureInfo.InvariantCulture)
        result.ExpiresAt <- DateTimeOffset.FromUnixTimeSeconds(fromNow)
        if resp.SettleDate <> null then
            result.PaidAt <- DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(resp.CreationDate, CultureInfo.InvariantCulture)) |> Nullable
            result
        else if result.ExpiresAt < DateTimeOffset.UtcNow then
            result
        else
            result
           

    type LNDSwaggerProvider with

        member this.AsILightningClient =
            {
                new ILightningClient with
                member __.GetInvoice (id: string, token: CancellationToken) = 
                    task {
                        return this.LookupInvoice(id) |> convertLndInvoice
                        }
                member __.CreateInvoice(amount: LightMoney, desc: string, expiry: TimeSpan, token) =
                    task {
                        return new LightningInvoice()
                        }
                member __.Listen (token) =
                    task {
                        return { new ILightningInvoiceListener with
                                member ___.Dispose() = ()
                                member ___.WaitInvoice(token) = task { return LightningInvoice() }
                            }
                        }
                member __.GetInfo (token) =
                    task {
                        return new LightningNodeInformation()
                    }
                member __.Pay(bolt11: string, token) =
                    task {
                        return new PayResponse(PayResult.Ok)
                    }
                member __.OpenChannel(request, token) =
                    task {
                        return new OpenChannelResponse(OpenChannelResult.AlreadyExists)
                    }
                member __.GetDepositAddress() =
                    task {
                        let addr = this.NewAddress()
                        let info = this.GetInfo()
                        return BitcoinAddress.Create(addr.Address)
                    }
                 member __.ConnectTo(info: NodeInfo) =
                     task {
                         return ()
                     } :> Task
            } 