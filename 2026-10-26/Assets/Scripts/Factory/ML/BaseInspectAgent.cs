using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using UnityEngine;

public class BaseInspectAgent : Agent
{
    #region 1. 에피소드 시작 (베이스 배치, 판단 요청)
    [Header("연결")]
    public AssemblySequence assembly;   // niryo_one의 AssemblySequence (베이스를 놓는 일을 맡김)

    [Header("모드")]
    [Tooltip("켜면 학습 모드: Play 하자마자 계속 반복. 끄면 화면의 '베이스 인식 평가' 버튼으로 시작")]
    public bool trainingMode = false;
    public float settleTime = 0.2f;   // 베이스를 놓은 뒤 대기 (초)

    bool running;   // 진행 중인지 (평가 모드: 버튼으로 켜짐 / 학습 모드: 처음부터 켜짐)
    bool waitingForDecision;   // 판단을 기다리는 중인지 (판단이 두 번 들어오는 것 방지)
    bool pendingEnd;   // 에피소드를 끝내야 하는지 (실제 종료는 FixedUpdate에서. 2번 region 참고)

    public override void Initialize()
    {
        if (assembly == null)
        {
            Debug.LogError("[BaseInspectAgent] Assembly가 비어 있습니다.");
        }
        running = trainingMode;
    }

    public override void OnEpisodeBegin()
    {
        StopAllCoroutines();
        StartCoroutine(BeginEpisode());
    }

    IEnumerator BeginEpisode()
    {
        waitingForDecision = false;
        pendingEnd = false;

        yield return new WaitUntil(() => running);
        yield return new WaitUntil(() => assembly.IsBusy == false && assembly.basePrefabs != null && assembly.basePrefabs.Length > 0);

        assembly.ClearPart();
        assembly.SpawnBase(Random.Range(0, assembly.basePrefabs.Length));

        yield return new WaitForSeconds(settleTime);
        yield return null;
        yield return null;

        waitingForDecision = true;
        RequestDecision();
    }
    #endregion

    #region 2. 판단과 보상 (답 고르기, 채점, 점수 주기)
    [Header("평가")]
    [Tooltip("평가 횟수 (0 = 계속)")]
    public int evalEpisodes = 100;
    [Tooltip("학습 모드에서 Console에 몇 회마다 요약을 남길지")]
    public int trainingLogEvery = 200;
    [Tooltip("평가 모드에서 틀린 경우만 Console에 남김 (끄면 매번)")]
    public bool logOnlyMistakes = true;

    [Header("보상")]
    public float correctReward = 1f;   // 종류와 방향이 모두 맞으면
    public float wrongReward = -1f;   // 하나라도 틀리면

    public PartType LastPrediction { get; private set; }   // 판단한 구멍 종류 -> 고를 부품 (Cube / Prism)
    public float LastHoleYaw { get; private set; }   // 판단한 구멍 방향 (0 또는 90도, 사각은 0)
    public bool LastCorrect { get; private set; }   // 맞혔는지

    const int ClassCount = 3;   // 답 개수
    static readonly PartType[] ClassToType = { PartType.Cube, PartType.Prism, PartType.Prism };   // 답 번호 -> 구멍 종류
    static readonly float[] ClassToYaw = { 0f, 0f, 90f };   // 답 번호 -> 구멍 방향
    static readonly string[] ClassName = { "사각", "삼각0도", "삼각90도" };   // 답 번호 -> 로그에 쓸 이름

    static int ClassOf(BaseInfo baseInfo)
    {
        return 0;
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (externalRequest)
        {
            externalRequest = false;
            int chosenClass = Mathf.Clamp(actions.DiscreteActions[0], 0, ClassCount - 1);
            LastPrediction = ClassToType[chosenClass];
            LastHoleYaw = ClassToYaw[chosenClass];
            BaseInfo currentBase = assembly.CurrentBase;
            Debug.Log($"[BaseDecide] 판단 {ClassName[chosenClass]} (구멍 방향 {LastHoleYaw:F0}도)" +
                      (currentBase != null ? $" | 실제 {ClassName[ClassOf(currentBase)]}, 베이스 회전 {currentBase.transform.eulerAngles.y:F0}도" : ""));
            return;
        }
        if (!waitingForDecision || assembly.CurrentBase == null)
        {
            return;
        }
        waitingForDecision = false;

        int predicted = Mathf.Clamp(actions.DiscreteActions[0], 0, ClassCount - 1);   // 모델이 고른 답
        int actual = ClassOf(assembly.CurrentBase);   // 정답
        bool correct = predicted == actual;   // 종류와 방향까지 맞혔나
        bool typeCorrect = ClassToType[predicted] == ClassToType[actual];   // 종류(사각/삼각)만이라도 맞혔나

        LastPrediction = ClassToType[predicted];
        LastHoleYaw = ClassToYaw[predicted];
        LastCorrect = correct;
        SetReward(correct ? correctReward : wrongReward);
        Record(actual, predicted, correct, typeCorrect);

        float baseYaw = assembly.CurrentBase.transform.eulerAngles.y;
        if (!trainingMode && saveDebugImages && (episodeCount <= saveFirstN || !correct))
        {
            SaveDebugImage($"{episodeCount:000}_실제{ClassName[actual]}_판단{ClassName[predicted]}_회전{baseYaw:F0}");
        }

        if (trainingMode)
        {
            Academy.Instance.StatsRecorder.Add("Base/Accuracy", correct ? 1f : 0f);
            Academy.Instance.StatsRecorder.Add("Base/TypeAccuracy", typeCorrect ? 1f : 0f);
            if (trainingLogEvery > 0 && episodeCount % trainingLogEvery == 0)
            {
                Debug.Log($"[BaseTrain] 최근 {recentResults.Count}회 정확도 {Pct(recentCorrectCount, recentResults.Count):F1}% | 누적 {episodeCount}회");
            }
        }
        else
        {
            if (!correct || !logOnlyMistakes)
            {
                Debug.Log($"[BaseEval] {episodeCount}회 | 실제 {ClassName[actual]} | 판단 {ClassName[predicted]}" +
                          $"{(correct ? "" : typeCorrect ? " (방향만 틀림)" : " *종류 오답")} | 베이스 회전 {baseYaw:F0}도");
            }
            if (evalEpisodes > 0 && episodeCount >= evalEpisodes)
            {
                running = false;
                Debug.Log($"[BaseEval] 평가 끝 | {Summary()}");
            }
        }
        pendingEnd = true;
    }

    void FixedUpdate()
    {
        if (!pendingEnd)
        {
            return;
        }
        pendingEnd = false;
        EndEpisode();
    }
    #endregion

    #region 3. 셀 운전: 요청 판단 (셀 컨트롤러가 부를 때 한 번 판단)
    bool externalRequest;   // 셀 컨트롤러가 판단을 요청한 상태
    public bool Deciding => externalRequest;   // 지금 판단 중인지

    public IEnumerator Decide()
    {
        if (externalRequest)
        {
            yield break;
        }
        if (assembly.CurrentBase == null)
        {
            Debug.LogWarning("[BaseDecide] 판단할 베이스가 없습니다 (AssemblySequence에 등록 필요).");
            yield break;
        }
        yield return null;
        yield return null;
        externalRequest = true;
        RequestDecision();
        yield return new WaitUntil(() => !externalRequest);
    }

    [ContextMenu("지금 장면 판단")]
    void MenuDecide()
    {
        if (Application.isPlaying)
        {
            StartCoroutine(Decide());
        }
    }
    #endregion

    #region 4. 결과 기록 (통계) - 핵심 흐름이 돌아간 뒤에 추가
    int episodeCount;   // 전체 판단 수
    int correctCount;   // 종류와 방향까지 맞힌 수
    int typeCorrectCount;   // 종류만이라도 맞힌 수

    readonly int[,] confusionTable = new int[ClassCount, ClassCount];

    const int RecentWindow = 200;
    readonly Queue<int> recentResults = new Queue<int>();   // 최근 결과 (맞힘 1, 틀림 0)
    int recentCorrectCount;   // 최근 결과 중 맞힌 수

    void Record(int actual, int predicted, bool correct, bool typeCorrect)
    {
        episodeCount++;
        if (correct)
        {
            correctCount++;
        }
        if (typeCorrect)
        {
            typeCorrectCount++;
        }

        confusionTable[actual, predicted]++;

        int result = correct ? 1 : 0;
        recentResults.Enqueue(result);
        recentCorrectCount += result;
        if (recentResults.Count > RecentWindow)
        {
            recentCorrectCount -= recentResults.Dequeue();
        }
    }

    static float Pct(int count, int total) => total > 0 ? 100f * count / total : 0f;

    string Summary()
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        text.Append($"{episodeCount}회 | 종류 정확도 {Pct(typeCorrectCount, episodeCount):F1}% | 종류+방향 {Pct(correctCount, episodeCount):F1}%");

        for (int actualClass = 0; actualClass < ClassCount; actualClass++)
        {
            int total = 0;
            for (int predictedClass = 0; predictedClass < ClassCount; predictedClass++)
            {
                total += confusionTable[actualClass, predictedClass];
            }
            text.Append($" | {ClassName[actualClass]} {confusionTable[actualClass, actualClass]}/{total}");
        }
        return text.ToString();
    }

    void ResetStats()
    {
        episodeCount = 0;
        correctCount = 0;
        typeCorrectCount = 0;
        System.Array.Clear(confusionTable, 0, confusionTable.Length);
        recentResults.Clear();
        recentCorrectCount = 0;
    }
    #endregion

    #region 5. 진단 이미지 (모델이 본 사진 저장)
    [Header("진단: 판단 순간 이미지 저장 (평가 모드)")]
    [Tooltip("켜면 평가할 때 JigCamera 이미지를 프로젝트 폴더/DebugImages에 PNG로 저장")]
    public bool saveDebugImages = false;
    public Camera jigCamera;   // JigCamera (진단용. Camera Sensor와 같은 카메라)
    [Tooltip("처음 몇 회는 정답도 저장 (비교용). 그 뒤로는 오답만")]
    public int saveFirstN = 20;

    void SaveDebugImage(string fileName)
    {
        if (jigCamera == null)
        {
            Debug.LogWarning("[BaseInspectAgent] Jig Camera가 비어 있어 이미지를 저장하지 못했습니다.");
            return;
        }

        RenderTexture renderTexture = RenderTexture.GetTemporary(84, 84, 24);
        RenderTexture previousTarget = jigCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        jigCamera.targetTexture = renderTexture;
        jigCamera.Render();

        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(84, 84, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 84, 84), 0, 0);
        texture.Apply();

        jigCamera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);

        string folder = System.IO.Path.Combine(Application.dataPath, "..", "DebugImages");
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, fileName + ".png"), texture.EncodeToPNG());
        Destroy(texture);
    }
    #endregion

    #region 6. 테스트 (새 변수 없음)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<int> discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = 0;
        if (assembly != null && assembly.CurrentBase != null)
        {
            discreteActions[0] = ClassOf(assembly.CurrentBase);
        }
    }
    #endregion

    #region 7. 화면 표시 (새 변수 없음. 4번 region의 통계를 표시)
    void OnGUI()
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        text.Append(trainingMode ? "[베이스 인식 학습]\n" : $"[베이스 인식 평가] {(running ? "진행 중" : "대기")}\n");
        text.Append(Summary());
        text.Append($"\n최근 {recentResults.Count}회 정확도 {Pct(recentCorrectCount, recentResults.Count):F1}%");

        float boxWidth = 640f;
        float boxHeight = 12 + 3 * 18;   // 위아래 여백 12 + 3줄 x 18
        Rect box = new Rect(10, Screen.height - boxHeight - 40, boxWidth, boxHeight);
        GUI.Box(box, text.ToString());

        if (trainingMode)
        {
            return;
        }

        GUI.enabled = !running;
        if (GUI.Button(new Rect(box.x, box.yMax + 4, 190, 26), evalEpisodes > 0 ? $"베이스 인식 평가 ({evalEpisodes}회)" : "베이스 인식 평가"))
        {
            ResetStats();
            running = true;
        }

        GUI.enabled = running;
        if (GUI.Button(new Rect(box.x + 196, box.yMax + 4, 100, 26), "멈춤"))
        {
            running = false;
        }
        GUI.enabled = true;
    }
    #endregion
}
