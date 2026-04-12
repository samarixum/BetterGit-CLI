namespace BetterGit;

public interface IProjectInitService {
    /* :: :: Contract :: START :: */

    void InitProject(string path, WebProjectOptions webProject);

    /* :: :: Contract :: END :: */
}
