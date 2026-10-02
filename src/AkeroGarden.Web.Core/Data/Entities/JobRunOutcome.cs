namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// How one nightly job run ended (FR-114, §8.17). <see cref="Failed"/> covers both a job that threw
/// and one that finished but could not send one or more of its emails — from the admin's side both
/// mean "something the system was meant to do did not happen".
/// </summary>
public enum JobRunOutcome
{
    Succeeded,
    Failed,
}
