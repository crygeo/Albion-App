using Albion_App._2Player;
using LibAlbionGame.Trackers;
using LibAlbionProtocol.Models;
using LibAlbionProtocol.PacketModels;
using LibAlbionRouting.Dispatching;
using LibAlbionRouting.Handlers;
using LibEvents.Entities;

namespace Albion_App.Infrastructure.Handlers;

public sealed class PartyResponseHandler : IResponseHandler
{
    private readonly PartyTracker _party;
    private readonly PlayerVm     _playerVm;

    public PartyResponseHandler(PartyTracker party, PlayerVm playerVm)
    {
        _party    = party;
        _playerVm = playerVm;
    }

    public void Register(IResponseDispatcher dispatcher)
        => dispatcher.Subscribe<JoinResponseModel>(OperationCodes.Join, OnJoin);

    private void OnJoin(JoinResponseModel model)
    {
        var player = new Player
        {
            ObjectId     = model.UserObjectId,
            Username     = model.Username,
            GuildName    = model.GuildName,
            AllianceName = model.AllianceName,
        };
        _party.OnJoin(player);
        _playerVm.UpdateFromJoin(player);
    }
}
