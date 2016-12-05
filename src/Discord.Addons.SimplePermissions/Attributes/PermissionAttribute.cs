﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;

namespace Discord.Addons.SimplePermissions
{
    /// <summary>
    /// Sets the permission level of this command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PermissionAttribute : PreconditionAttribute
    {
        private MinimumPermission Permission { get; }

        /// <summary>
        /// Sets the permission level of this command.
        /// </summary>
        /// <param name="minimum">The <see cref="MinimumPermission"/> requirement.</param>
        public PermissionAttribute(MinimumPermission minimum)
        {
            Permission = minimum;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="command"></param>
        /// <param name="map"></param>
        /// <returns></returns>
        public override async Task<PreconditionResult> CheckPermissions(CommandContext context, CommandInfo command, IDependencyMap map)
        {
            if (context.IsPrivate)
                return PreconditionResult.FromSuccess();

            var cfg = map.Get<PermissionsService>().ConfigStore.Load();
            var chan = context.Channel;
            var user = context.User;
            var ownerId = (await context.Client.GetApplicationInfoAsync()).Owner.Id;

            if (cfg != null)
            {
                if (cfg.GetChannelModuleWhitelist(chan.Id).Contains(command.Module.Name))
                {
                    if (Permission == MinimumPermission.BotOwner &&
                        user.Id == ownerId)
                    {
                        return PreconditionResult.FromSuccess();
                    }
                    else if (Permission == MinimumPermission.Special &&
                        cfg.GetSpecialPermissionUsersList(chan.Id).Contains(user.Id))
                    {
                        return PreconditionResult.FromSuccess();
                    }
                    else if (Permission <= MinimumPermission.GuildOwner &&
                        context.Guild?.OwnerId == user.Id)
                    {
                        return PreconditionResult.FromSuccess();
                    }
                    else if (Permission <= MinimumPermission.AdminRole &&
                        (user as IGuildUser)?.RoleIds.Any(r => r == cfg.GetGuildAdminRole(context.Guild.Id)) == true)
                    {
                        return PreconditionResult.FromSuccess();
                    }
                    else if (Permission <= MinimumPermission.ModRole &&
                        (user as IGuildUser)?.RoleIds.Any(r => r == cfg.GetGuildModRole(context.Guild.Id)) == true)
                    {
                        return PreconditionResult.FromSuccess();
                    }
                    else if (Permission == MinimumPermission.Everyone)
                    {
                        return PreconditionResult.FromSuccess();
                    }
                    else
                    {
                        return PreconditionResult.FromError("Insufficient permission.");
                    }
                }
                else
                    return PreconditionResult.FromError("Command not whitelisted");
            }
            else
                return PreconditionResult.FromError("No config found.");
        }
    }
}
