using LibAlbionGame.Combat;
using LibAlbionProtocol.Models;
using LibAlbionProtocol.PacketModels;
using LibAlbionRouting.Dispatching;
using LibAlbionRouting.Handlers;

namespace Albion_App.Infrastructure.Handlers;

public sealed class CombatEventHandler : IEventHandler
{
    private readonly CombatSession _session;

    public CombatEventHandler(CombatSession session) => _session = session;

    public void Register(IEventDispatcher dispatcher)
    {
        dispatcher.Subscribe<HealthUpdateModel>(
            EventCodes.HealthUpdate,
            m =>
            {
                _session.RecordHealthUpdate(m.CauserId, m.AffectedObjectId, m.HealthChange);
                return Task.CompletedTask;
            });

        dispatcher.Subscribe<InCombatStateUpdateModel>(
            EventCodes.InCombatStateUpdate,
            m =>
            {
                _session.UpdateCombatState(m.ObjectId, m.IsInCombat);
                return Task.CompletedTask;
            });
    }
}
