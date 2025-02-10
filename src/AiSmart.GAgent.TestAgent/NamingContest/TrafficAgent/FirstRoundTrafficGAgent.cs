using AISmart.Agent;
using AISmart.Agent.GEvents;
using AISmart.Agents;
using AISmart.Agents.Group;
using AISmart.GAgent.Core;
using AiSmart.GAgent.TestAgent.NamingContest.Common;
using AiSmart.GAgent.TestAgent.NamingContest.JudgeAgent;
using AiSmart.GAgent.TestAgent.NamingContest.VoteAgent;
using AISmart.Grains;
using AISmart.Sender;
using AutoGen.Core;
using Google.Cloud.AIPlatform.V1;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AiSmart.GAgent.TestAgent.NamingContest.TrafficAgent;

public class FirstRoundTrafficGAgent : GAgentBase<FirstTrafficState, TrafficEventSourcingBase>, IFirstTrafficGAgent
{
    public FirstRoundTrafficGAgent(ILogger<FirstRoundTrafficGAgent> logger) : base(logger)
    {
    }

    [EventHandler]
    public async Task HandleEventAsync(GroupStartEvent @event)
    {
        if ((int)State.NamingStep >= (int)NamingContestStepEnum.Naming)
        {
            Logger.LogWarning("[FirstRoundTrafficGAgent] GroupStartEvent has processed");
            return;
        }

        Logger.LogInformation($"{this.GetGrainId().ToString()}: [FirstRoundTrafficGAgent] GroupStartEvent Start");
        RaiseEvent(new TrafficNameStartSEvent { Content = @event.Message });
        RaiseEvent(new ChangeNamingStepSEvent { Step = NamingContestStepEnum.NamingStart });
        RaiseEvent(new AddChatHistorySEvent()
            { ChatMessage = new MicroAIMessage(Role.User.ToString(), @event.Message) });
        await base.ConfirmEvents();

        await PublishAsync(new NamingLogEvent(NamingContestStepEnum.NamingStart, Guid.Empty));
        await PublishAsync(new GroupChatStartGEvent() { IfFirstStep = true, ThemeDescribe = @event.Message });
        await DispatchCreativeAgent();

        RaiseEvent(new ChangeNamingStepSEvent { Step = NamingContestStepEnum.Naming });
        await base.ConfirmEvents();

        // Logger.LogInformation("[FirstRoundTrafficGAgent] GroupStartEvent End");
    }

    [EventHandler]
    public async Task HandleEventAsync(NamedCompleteGEvent @event)
    {
        if (State.CurrentGrainId != @event.GrainGuid)
        {
            Logger.LogError(
                $"Traffic NamedCompleteGEvent Current GrainId not match {State.CurrentGrainId.ToString()}--{@event.GrainGuid.ToString()}");
            return;
        }

        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] NamedCompleteGEvent start GrainId:{this.GetPrimaryKey().ToString()} ");
        base.RaiseEvent(new TrafficGrainCompleteSEvent()
        {
            CompleteGrainId = @event.GrainGuid,
        });

        base.RaiseEvent(new AddChatHistorySEvent()
        {
            ChatMessage = new MicroAIMessage(Role.User.ToString(),
                AssembleMessageUtil.AssembleNamingContent(@event.CreativeName, @event.NamingReply))
        });

        base.RaiseEvent(new CreativeNamingSEvent() { CreativeId = @event.GrainGuid, Naming = @event.NamingReply });

        await base.ConfirmEvents();

        await DispatchCreativeAgent();

        // Logger.LogInformation($"[FirstRoundTrafficGAgent] NamedCompleteGEvent End GrainId:{this.GetPrimaryKey().ToString()} ");
    }

    [EventHandler]
    public async Task HandleEventAsync(DebatedCompleteGEvent @event)
    {
        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] DebatedCompleteGEvent start GrainId:{this.GetPrimaryKey().ToString()} ");
        if (State.CurrentGrainId != @event.GrainGuid)
        {
            Logger.LogError(
                $"[FirstRoundTrafficGAgent] DebatedCompleteGEvent Current GrainId not match {State.CurrentGrainId.ToString()}--{@event.GrainGuid.ToString()}");
            return;
        }

        base.RaiseEvent(new AddChatHistorySEvent()
        {
            ChatMessage = new MicroAIMessage(Role.User.ToString(),
                AssembleMessageUtil.AssembleDebateContent(@event.CreativeName, @event.DebateReply))
        });

        base.RaiseEvent(new TrafficGrainCompleteSEvent()
        {
            CompleteGrainId = @event.GrainGuid,
        });

        await base.ConfirmEvents();

        await DispatchDebateAgent();
        // Logger.LogInformation($"[FirstRoundTrafficGAgent] DebatedCompleteGEvent End GrainId:{this.GetPrimaryKey().ToString()} ");
    }

    [EventHandler]
    public async Task HandleEventAsync(JudgeVoteResultGEvent @event)
    {
        if (State.CurrentGrainId != @event.JudgeGrainId)
        {
            Logger.LogError(
                $"[FirstRoundTrafficGAgent] JudgeVoteResultGEvent Current GrainId not match {State.CurrentGrainId.ToString()}--{@event.JudgeGrainId.ToString()}");
            return;
        }

        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] JudgeVoteResultGEvent Start GrainId:{this.GetPrimaryKey().ToString()} ");
        var creativeInfo = State.CreativeList.FirstOrDefault(f => f.Naming == @event.VoteName);
        JudgeVoteInfo voteInfo;
        if (creativeInfo != null)
        {
            voteInfo = new JudgeVoteInfo()
            {
                AgentId = creativeInfo.CreativeGrainId, AgentName = creativeInfo.CreativeName,
                Nameing = @event.VoteName, Reason = @event.Reason
            };
        }
        else
        {
            creativeInfo = State.CreativeList[0];
            voteInfo = new JudgeVoteInfo()
            {
                AgentId = creativeInfo.CreativeGrainId, AgentName = creativeInfo.CreativeName,
                Nameing = creativeInfo.Naming, Reason = NamingConstants.CreativeGroupSummaryReason
            };
        }

        var voteInfoStr = JsonConvert.SerializeObject(voteInfo);
        await PublishAsync(new NamingLogEvent(NamingContestStepEnum.JudgeVote, @event.RealJudgeGrainId,
            NamingRoleType.Judge, @event.JudgeName, voteInfoStr));

        base.RaiseEvent(new TrafficGrainCompleteSEvent()
        {
            CompleteGrainId = @event.JudgeGrainId,
        });

        await base.ConfirmEvents();

        await DispatchJudgeAgent();
        // Logger.LogInformation($"[FirstRoundTrafficGAgent] JudgeVoteResultGEvent End GrainId:{this.GetPrimaryKey().ToString()} ");
    }

    [EventHandler]
    public async Task HandleEventAsync(HostSummaryCompleteGEvent @event)
    {
        if (State.CurrentGrainId != @event.HostId)
        {
            Logger.LogError(
                $"[FirstRoundTrafficGAgent] HostSummaryCompleteGEvent Current GrainId not match {State.CurrentGrainId.ToString()}--{@event.HostId.ToString()}");
            return;
        }

        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] HostSummaryCompleteGEvent Start GrainId:{this.GetPrimaryKey().ToString()} ");
        base.RaiseEvent(new TrafficGrainCompleteSEvent()
        {
            CompleteGrainId = @event.HostId,
        });

        await base.ConfirmEvents();

        await DispatchHostAgent();

        // Logger.LogInformation($"[FirstRoundTrafficGAgent] HostSummaryCompleteGEvent End GrainId:{this.GetPrimaryKey().ToString()} ");
    }

    public Task<MicroAIGAgentState> GetStateAsync()
    {
        throw new NotImplementedException();
    }

    public override Task<string> GetDescriptionAsync()
    {
        throw new NotImplementedException();
    }

    private async Task DispatchCreativeAgent()
    {
        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] DispatchCreativeAgent GrainId:{this.GetPrimaryKey().ToString()}");
        var random = new Random();
        var creativeList = State.CreativeList.FindAll(f => State.CalledGrainIdList.Contains(f.CreativeGrainId) == false)
            .ToList();
        if (creativeList.Count == 0)
        {
            // Logger.LogInformation($"[FirstRoundTrafficGAgent] DispatchCreativeAgent Over GrainId:{this.GetPrimaryKey().ToString()}");
            await PublishAsync(new NamingLogEvent(NamingContestStepEnum.DebateStart, Guid.Empty));

            // end message 
            await PublishAsync(new TrafficNamingContestOver() { NamingQuestion = State.NamingContent });

            RaiseEvent(new ClearCalledGrainsSEvent());

            var debateRound = random.Next(1, 3);
            RaiseEvent(new SetDebateCountSEvent() { DebateCount = debateRound });
            RaiseEvent(new ChangeNamingStepSEvent { Step = NamingContestStepEnum.Debate });

            await base.ConfirmEvents();

            // begin the second stage debate
            _ = DispatchDebateAgent();

            return;
        }

        // random select one Agent
        var index = random.Next(0, creativeList.Count);
        var selectedInfo = creativeList[index];
        RaiseEvent(new TrafficCallSelectGrainIdSEvent() { GrainId = selectedInfo.CreativeGrainId });
        await base.ConfirmEvents();

        // route to the selectedId Agent 
        await PublishAsync(new TrafficInformCreativeGEvent()
            { CreativeGrainId = selectedInfo.CreativeGrainId });
    }

    private async Task DispatchDebateAgent()
    {
        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] DispatchDebateAgent GrainId:{this.GetPrimaryKey().ToString()}");
        // the second stage - debate
        var creativeList = State.CreativeList.FindAll(f => State.CalledGrainIdList.Contains(f.CreativeGrainId) == false)
            .ToList();
        if (State.DebateRoundCount == 0 && creativeList.Count == 0)
        {
            Logger.LogInformation(
                $"[FirstRoundTrafficGAgent] DispatchDebateAgent Over GrainId:{this.GetPrimaryKey().ToString()}");
            await PublishAsync(new NamingLogEvent(NamingContestStepEnum.JudgeVoteStart, Guid.Empty));

            RaiseEvent(new ChangeNamingStepSEvent { Step = NamingContestStepEnum.JudgeVoteStart });
            RaiseEvent(new ClearCalledGrainsSEvent());
            await base.ConfirmEvents();

            // begin the third stage judge
            _ = DispatchJudgeAgent();
            return;
        }

        if (creativeList.Count == 0 && State.DebateRoundCount > 0)
        {
            creativeList = State.CreativeList;
            RaiseEvent(new ReduceDebateRoundSEvent());
            RaiseEvent(new ClearCalledGrainsSEvent());
        }

        // random select one Agent
        var random = new Random();
        var index = random.Next(0, creativeList.Count);
        var selectedInfo = creativeList[index];
        RaiseEvent(new TrafficCallSelectGrainIdSEvent() { GrainId = selectedInfo.CreativeGrainId });
        await base.ConfirmEvents();

        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] DispatchDebateAgent GrainId:{this.GetPrimaryKey().ToString()}, creative:{selectedInfo.CreativeGrainId.ToString()}");
        // route to the selectedId Agent 
        await PublishAsync(new TrafficInformDebateGEvent()
            { CreativeGrainId = selectedInfo.CreativeGrainId });
    }

    private async Task DispatchJudgeAgent()
    {
        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] DispatchJudgeAgent GrainId:{this.GetPrimaryKey().ToString()}");
        var creativeList = State.JudgeAgentList.FindAll(f => State.CalledGrainIdList.Contains(f) == false).ToList();
        if (creativeList.Count == 0)
        {
            // Logger.LogInformation($"[FirstRoundTrafficGAgent] DispatchJudgeAgent Over GrainId:{this.GetPrimaryKey().ToString()}");
            await PublishAsync(new NamingLogEvent(NamingContestStepEnum.Complete, Guid.Empty));
            await PublishAsync(new NamingContestComplete());
            RaiseEvent(new ChangeNamingStepSEvent { Step = NamingContestStepEnum.Complete });
            await PublishMostCharmingEventAsync();
            await DispatchHostAgent();
            return;
        }

        var random = new Random();
        var index = random.Next(0, creativeList.Count);
        var selectedId = creativeList[index];
        RaiseEvent(new TrafficCallSelectGrainIdSEvent() { GrainId = selectedId });
        await base.ConfirmEvents();

        Logger.LogInformation(
            $"[FirstRoundTrafficGAgent] DispatchJudgeAgent GrainId:{this.GetPrimaryKey().ToString()}, Judge:{selectedId.ToString()}");
        await PublishAsync(new JudgeVoteGEVent() { JudgeGrainId = selectedId, History = State.ChatHistory });
    }

    private async Task PublishMostCharmingEventAsync()
    {
        IVoteCharmingGAgent voteCharmingGAgent =
            GrainFactory.GetGrain<IVoteCharmingGAgent>(Helper.GetVoteCharmingGrainId(1,
                State.Step));

        GrainId grainId = await voteCharmingGAgent.GetParentAsync();

        IPublishingGAgent publishingAgent;

        if (grainId != null && grainId.ToString().StartsWith("publishinggagent"))
        {
            publishingAgent = GrainFactory.GetGrain<IPublishingGAgent>(grainId);
        }
        else
        {
            publishingAgent = GrainFactory.GetGrain<IPublishingGAgent>(Guid.NewGuid());
            await publishingAgent.RegisterAsync(voteCharmingGAgent);
        }

        await publishingAgent.PublishEventAsync(new VoteCharmingEvent()
        {
            AgentIdNameDictionary = State.CreativeList.ToDictionary(p => p.CreativeGrainId, p => p.CreativeName),
            Round = 1,
            VoteMessage = State.ChatHistory
        });
        // Logger.LogInformation("VoteCharmingEvent send");
    }

    private async Task DispatchHostAgent()
    {
        var hostAgentList = State.HostAgentList.FindAll(f => State.CalledGrainIdList.Contains(f) == false).ToList();
        if (hostAgentList.Count == 0)
        {
            await PublishAsync(new NamingLogEvent(NamingContestStepEnum.HostSummaryComplete, Guid.Empty));
            return;
        }

        var random = new Random();
        var index = random.Next(0, hostAgentList.Count);
        var selectedId = hostAgentList[index];
        RaiseEvent(new TrafficCallSelectGrainIdSEvent() { GrainId = selectedId });
        await base.ConfirmEvents();

        await PublishToHostGAgentGroup(selectedId);


        // await PublishAsync(new HostSummaryGEvent() { HostId = selectedId, History = State.ChatHistory });
    }

    private async Task PublishToHostGAgentGroup(Guid selectedId)
    {
        var hostGroupGAgentId = Helper.GetHostGroupGrainId();
        var hostGroupGAgent = GrainFactory.GetGrain<IStateGAgent<GroupAgentState>>(hostGroupGAgentId);

        GrainId grainId = await hostGroupGAgent.GetParentAsync();

        IPublishingGAgent publishingAgent;

        if (grainId != null && grainId.ToString().StartsWith("publishinggagent"))
        {
            publishingAgent = GrainFactory.GetGrain<IPublishingGAgent>(grainId);
        }
        else
        {
            publishingAgent = GrainFactory.GetGrain<IPublishingGAgent>(Guid.NewGuid());
            await publishingAgent.RegisterAsync(hostGroupGAgent);
        }

        await publishingAgent.PublishEventAsync(
            new HostSummaryGEvent()
                { HostId = selectedId, History = State.ChatHistory, GroupId = await this.GetParentAsync() });
    }

    public async Task SetAgent(string agentName, string agentResponsibility)
    {
        RaiseEvent(new TrafficSetAgentSEvent
        {
            AgentName = agentName,
            Description = agentResponsibility
        });
        await ConfirmEvents();

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
        RaiseEvent(new TrafficSetAgentSEvent
        {
            AgentName = agentName,
            Description = agentResponsibility
        });
        await ConfirmEvents();

        await GrainFactory.GetGrain<IChatAgentGrain>(agentName)
            .SetAgentWithTemperature(agentResponsibility, temperature, seed, maxTokens);
    }

    public Task<MicroAIGAgentState> GetAgentState()
    {
        throw new NotImplementedException();
    }

    public async Task AddCreativeAgent(string creativeName, Guid creativeGrainId)
    {
        RaiseEvent(new AddCreativeAgent() { CreativeGrainId = creativeGrainId, CreativeName = creativeName });
        await base.ConfirmEvents();
    }

    public async Task AddJudgeAgent(Guid judgeGrainId)
    {
        RaiseEvent(new AddJudgeSEvent() { JudgeGrainId = judgeGrainId });
        await ConfirmEvents();
    }

    public async Task AddHostAgent(Guid judgeGrainId)
    {
        RaiseEvent(new AddHostSEvent() { HostGrainId = judgeGrainId });
        await ConfirmEvents();
    }

    public async Task SetStepCount(int step)
    {
        RaiseEvent(new SetStepNumberSEvent() { StepCount = step });
        await ConfirmEvents();
    }

    public Task<int> GetProcessStep()
    {
        return Task.FromResult((int)State.NamingStep);
    }

    protected override void TransitionState(FirstTrafficState state, object @event)
    {
        switch (@event)
        {
            case TrafficCallSelectGrainIdSEvent sEvent:
                State.CurrentGrainId = sEvent.GrainId;
                Logger.LogInformation($"Call Storage State,CurrentGrainId:{sEvent.GrainId}");
                break;
            case TrafficNameStartSEvent sEvent:
                State.NamingContent = sEvent.Content;
                break;
            case TrafficGrainCompleteSEvent sEvent:
                State.CalledGrainIdList.Add(sEvent.CompleteGrainId);
                State.CurrentGrainId = Guid.Empty;
                break;
        }

        base.TransitionState(state, @event);
    }
}