using GameFramework.Net.Protocol;

namespace GameFramework.Business
{
    /// <summary>
    /// 宠物系统：负责 player.pets.push（全量）和 player.pets.changed（增量）。
    /// 宠物是账号收藏的图鉴道具（独立字段，不混进背包），处理方式和 BagSystem 一致：
    /// 收到推送 → 写 PlayerModel.Pets → 发 PetListChangedEvent / PetChangedEvent 给界面。
    /// </summary>
    public sealed class PetSystem : GameSystem
    {
        private readonly PlayerModel model;

        public PetSystem(PlayerModel model)
        {
            this.model = model;
        }

        protected override void OnBind()
        {
            Subscribe<PetListPush>(ProtocolId.PetListPush, OnPetListPush);
            Subscribe<PetChangedPush>(ProtocolId.PetChangedPush, OnPetChangedPush);
        }

        private void OnPetListPush(PetListPush push)
        {
            if (push == null) return;

            HandledCount++;
            model.ApplyPets(push);

            Publish(new PetListChangedEvent { Version = model.Version });
        }

        private void OnPetChangedPush(PetChangedPush push)
        {
            if (push == null) return;

            HandledCount++;
            model.ApplyPet(push);

            Publish(new PetChangedEvent
            {
                PetId = push.PetId,
                Added = push.Added,
                Total = push.Total,
                Version = model.Version
            });
        }
    }
}
