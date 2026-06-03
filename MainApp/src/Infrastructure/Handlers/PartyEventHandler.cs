using LibAlbionGame.Interfaces;
using LibAlbionGame.Mapping;
using LibAlbionGame.Trackers;
using LibAlbionProtocol.Models;
using LibAlbionProtocol.PacketModels;
using LibAlbionRouting.Dispatching;
using LibAlbionRouting.Handlers;
using LibEvents.Entities;

namespace Albion_App.Infrastructure.Handlers;

public sealed class PartyEventHandler : IEventHandler
{
    private readonly PartyTracker        _party;
    private readonly IItemIndexResolver? _items;

    public PartyEventHandler(PartyTracker party, IItemIndexResolver? items = null)
    {
        _party = party;
        _items = items;
    }

    public void Register(IEventDispatcher dispatcher)
    {
        dispatcher.Subscribe<NewCharacterModel>(
            EventCodes.NewCharacter,
            m =>
            {
                _party.OnNewCharacter(new Player
                {
                    ObjectId  = m.ObjectId,
                    Username  = m.Name,
                    GuildName = m.GuildName,
                    Equipment = EquipmentMapper.ToBuild(m.Equipment, _items),
                });
                return Task.CompletedTask;
            });

        dispatcher.Subscribe<PartyJoinedModel>(
            EventCodes.PartyJoined,
            m =>
            {
                List<Player> players = [];

                foreach (var user in m.Usernames)
                    players.Add(new Player{Username =  user});
                _party.OnPartyJoined(players.ToArray());
                return Task.CompletedTask;
            });

        dispatcher.Subscribe<PartyPlayerJoinedModel>(
            EventCodes.PartyPlayerJoined,
            m =>
            {
                _party.OnPartyPlayerJoined(new Player
                {
                    ObjectId = m.ObjectId,
                    Username = m.Username,
                });
                return Task.CompletedTask;
            });

        dispatcher.Subscribe<PartyPlayerLeftModel>(
            EventCodes.PartyPlayerLeft,
            m => { _party.OnPartyPlayerLeft(m.ObjectId); return Task.CompletedTask; });

        dispatcher.Subscribe<PartyDisbandedModel>(
            EventCodes.PartyDisbanded,
            m => { _party.OnPartyDisbanded(); return Task.CompletedTask; });

        dispatcher.Subscribe<PartyInvitationAnswerModel>(
            EventCodes.PartyInvitationAnswer,
            m =>
            {
                if (m.Accepted) _party.OnPartyInvitationAnswer(m.Username);
                return Task.CompletedTask;
            });

        dispatcher.Subscribe<CharacterEquipmentChangedModel>(
            EventCodes.CharacterEquipmentChanged,
            m =>
            {
                _party.OnEquipmentChanged(m.ObjectId, EquipmentMapper.ToBuild(m.Equipment, _items));
                return Task.CompletedTask;
            });
    }
}
