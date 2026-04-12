namespace BetterGit;

public sealed class ProjectInitServiceAdapter : IProjectInitService {
    /* :: :: Methods :: START :: */

    public void InitProject(string path, WebProjectOptions webProject) {
        ProjectInitService.InitProject(path, webProject);
    }

    /* :: :: Methods :: END :: */
}
