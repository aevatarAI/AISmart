using AISmart.Agent;
using AISmart.Agent.GEvents;
using AISmart.Agents;
using AISmart.Agents.Group;
using AISmart.GAgent.Core;
using AiSmart.GAgent.TestAgent.NamingContest.Common;
using AiSmart.GAgent.TestAgent.NamingContest.TrafficAgent;
using AISmart.Grains;
using AISmart.Sender;
using AutoGen.Core;
using Microsoft.Extensions.Logging;

namespace AiSmart.GAgent.TestAgent.NamingContest.HostAgent;

public class HostGAgent : GAgentBase<HostState, HostSEventBase>, IHostGAgent
{
    private readonly ILogger<HostGAgent> _logger;

    public HostGAgent(ILogger<HostGAgent> logger) : base(logger)
    {
        _logger = logger;
    }

    [EventHandler]
    public async Task HandleEventAsync(HostSummaryGEvent @event)
    {
        if (@event.HostId != this.GetPrimaryKey())
        {
            return;
        }

        var summaryReply = string.Empty;
        var prompt = NamingConstants.SummaryPrompt;
        try
        {
            
            var response = await GrainFactory.GetGrain<IChatAgentGrain>(State.AgentName)
                .SendAsync(prompt, @event.History);

            if (response != null && !response.Content.IsNullOrEmpty())
            {
                summaryReply = response.Content;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Creative] TrafficInformCreativeGEvent error");
            summaryReply = NamingConstants.DefaultCreativeNaming;
        }
        finally
        {
            var groupGAgentId = @event.GroupId;
            var groupGAgent = GrainFactory.GetGrain<IStateGAgent<GroupAgentState>>(groupGAgentId);
            
            GrainId grainId = await groupGAgent.GetParentAsync();

            IPublishingGAgent publishingAgent;

            if (grainId != null && grainId.ToString().StartsWith("publishinggagent"))
            {
                publishingAgent = GrainFactory.GetGrain<IPublishingGAgent>(grainId);
            }
            else
            {
                publishingAgent = GrainFactory.GetGrain<IPublishingGAgent>(Guid.NewGuid());
                await publishingAgent.RegisterAsync(groupGAgent);
            }

            await publishingAgent.PublishEventAsync(new HostSummaryCompleteGEvent()
            {
                HostId = this.GetPrimaryKey(),
                SummaryReply = summaryReply,
                HostName = State.AgentName,
            });
            
            await publishingAgent.PublishEventAsync(new NamingAILogEvent(
                NamingContestStepEnum.HostSummary,
                this.GetPrimaryKey(),
                NamingRoleType.Host, 
                State.AgentName,
                summaryReply, 
                prompt));
            

            // await this.PublishAsync(new HostSummaryCompleteGEvent()
            // {
            //     HostId = this.GetPrimaryKey(),
            //     SummaryReply = summaryReply,
            //     HostName = State.AgentName,
            // });
            
            // await PublishAsync(new NamingAILogEvent(NamingContestStepEnum.HostSummary, this.GetPrimaryKey(),
            //     NamingRoleType.Host, State.AgentName, summaryReply, prompt));

            RaiseEvent(new AddHistoryChatSEvent()
            {
                Message = new MicroAIMessage(Role.User.ToString(),
                    AssembleMessageUtil.AssembleNamingContent(State.AgentName, summaryReply))
            });
            

            await base.ConfirmEvents();
        }
    }

    public Task<MicroAIGAgentState> GetStateAsync()
    {
        throw new NotImplementedException();
    }

    public override Task<string> GetDescriptionAsync()
    {
        throw new NotImplementedException();
    }

    public async Task SetAgent(string agentName, string agentResponsibility)
    {
        RaiseEvent(new SetAgentInfoSEvent { AgentName = agentName, Description = agentResponsibility });
        await base.ConfirmEvents();

        await GrainFactory.GetGrain<IChatAgentGrain>(agentName).SetAgentAsync(agentResponsibility);
    }

    public Task SetAgentWithLLm(string agentName, string agentResponsibility, string llm)
    {
        throw new NotImplementedException();
    }

    public async Task SetAgentWithTemperatureAsync(string agentName, string agentResponsibility, float temperature,
        int? seed = null,
        int? maxTokens = null)
    {
        RaiseEvent(new SetAgentInfoSEvent { AgentName = agentName, Description = agentResponsibility });
        await base.ConfirmEvents();

        await GrainFactory.GetGrain<IChatAgentGrain>(agentName)
            .SetAgentWithTemperature(agentResponsibility, temperature, seed, maxTokens);
    }

    public Task<MicroAIGAgentState> GetAgentState()
    {
        throw new NotImplementedException();
    }

    public Task<string> GetCreativeNaming()
    {
        return Task.FromResult(State.Naming);
    }

    public Task<string> GetCreativeName()
    {
        return Task.FromResult(State.AgentName);
    }
}