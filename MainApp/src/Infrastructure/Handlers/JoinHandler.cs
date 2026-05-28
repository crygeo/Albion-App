using Albion_App._2Player;
using LibAlbionProtocol.Models;
using LibAlbionProtocol.PacketModels;
using LibAlbionRouting.Dispatching;
using LibAlbionRouting.Handlers;

namespace Albion_App.Infrastructure.Handlers;

public sealed class JoinHandler : IResponseHandler
{
    private readonly PlayerVm _playerVm;

    public JoinHandler(PlayerVm playerVm)
        => _playerVm = playerVm;

    public void Register(IResponseDispatcher dispatcher)
        => dispatcher.Subscribe<JoinModel>(OperationCodes.Join, OnJoin);

    private void OnJoin(JoinModel model)
        => _playerVm.UpdateFromJoin(model);
}
