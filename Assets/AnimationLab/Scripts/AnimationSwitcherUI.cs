using UnityEngine;
using UnityEngine.UIElements;

namespace GarmentDemo.Sandbox
{
    public sealed class AnimationSwitcherUI : MonoBehaviour
    {
        private const string ActiveMotionClass = "motion-button--active";
        private const string ActivePauseClass = "pause-button--active";

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private Animator[] animationTargets;
        [SerializeField] private GameObject[] topOutfits;
        [SerializeField] private GameObject[] bottomOutfits;
        [SerializeField] private float blendDuration = 0.35f;

        private Button idleButton;
        private Button walkingButton;
        private Button actionButton;
        private Button tPoseButton;
        private Button pauseButton;
        private Button collapseButton;
        private Button topPreviousButton;
        private Button topNextButton;
        private Button bottomPreviousButton;
        private Button bottomNextButton;
        private Toggle clothToggle;
        private Slider blendSlider;
        private Label currentStateLabel;
        private Label statusLabel;
        private Label blendValueLabel;
        private Label topOutfitName;
        private Label bottomOutfitName;
        private VisualElement previewContent;
        private SkirtClothController skirtCloth;
        private DressClothController dressCloth;
        private string currentState = "Idle";
        private bool paused;
        private bool collapsed;
        private int topOutfitIndex = 2;
        private int bottomOutfitIndex;

        private void OnEnable()
        {
            topOutfitIndex = 2;
            bottomOutfitIndex = 0;

            VisualElement root = uiDocument.rootVisualElement;
            idleButton = root.Q<Button>("idle-button");
            walkingButton = root.Q<Button>("walking-button");
            actionButton = root.Q<Button>("action-button");
            tPoseButton = root.Q<Button>("tpose-button");
            pauseButton = root.Q<Button>("pause-button");
            collapseButton = root.Q<Button>("collapse-button");
            topPreviousButton = root.Q<Button>("top-previous-button");
            topNextButton = root.Q<Button>("top-next-button");
            bottomPreviousButton = root.Q<Button>("bottom-previous-button");
            bottomNextButton = root.Q<Button>("bottom-next-button");
            clothToggle = root.Q<Toggle>("cloth-toggle");
            blendSlider = root.Q<Slider>("blend-slider");
            currentStateLabel = root.Q<Label>("current-state");
            statusLabel = root.Q<Label>("status-label");
            blendValueLabel = root.Q<Label>("blend-value");
            topOutfitName = root.Q<Label>("top-outfit-name");
            bottomOutfitName = root.Q<Label>("bottom-outfit-name");
            previewContent = root.Q<VisualElement>("preview-content");
            skirtCloth = bottomOutfits[0].GetComponentInChildren<SkirtClothController>(true);
            dressCloth = topOutfits[1].GetComponentInChildren<DressClothController>(true);

            idleButton.clicked += PlayIdle;
            walkingButton.clicked += PlayWalking;
            actionButton.clicked += PlayAction;
            tPoseButton.clicked += PlayTPose;
            pauseButton.clicked += TogglePause;
            collapseButton.clicked += ToggleCollapsed;
            topPreviousButton.clicked += SelectPreviousTop;
            topNextButton.clicked += SelectNextTop;
            bottomPreviousButton.clicked += SelectPreviousBottom;
            bottomNextButton.clicked += SelectNextBottom;
            clothToggle.RegisterValueChangedCallback(OnClothChanged);
            blendSlider.RegisterValueChangedCallback(OnBlendChanged);

            clothToggle.SetValueWithoutNotify(dressCloth.IsClothEnabled);
            blendSlider.value = blendDuration;
            UpdateBlendLabel();
            SwitchTo("T-Pose", tPoseButton);
            UpdateTopOutfit();
            UpdateBottomOutfit();
        }

        private void OnDisable()
        {
            idleButton.clicked -= PlayIdle;
            walkingButton.clicked -= PlayWalking;
            actionButton.clicked -= PlayAction;
            tPoseButton.clicked -= PlayTPose;
            pauseButton.clicked -= TogglePause;
            collapseButton.clicked -= ToggleCollapsed;
            topPreviousButton.clicked -= SelectPreviousTop;
            topNextButton.clicked -= SelectNextTop;
            bottomPreviousButton.clicked -= SelectPreviousBottom;
            bottomNextButton.clicked -= SelectNextBottom;
            clothToggle.UnregisterValueChangedCallback(OnClothChanged);
            blendSlider.UnregisterValueChangedCallback(OnBlendChanged);
        }

        private void PlayIdle()
        {
            SwitchTo("Idle", idleButton);
        }

        private void PlayWalking()
        {
            SwitchTo("Walking", walkingButton);
        }

        private void PlayAction()
        {
            SwitchTo("Action", actionButton);
        }

        private void PlayTPose()
        {
            SwitchTo("T-Pose", tPoseButton);
        }

        private void SelectPreviousTop()
        {
            topOutfitIndex = (topOutfitIndex + 2) % 3;
            UpdateTopOutfit();
        }

        private void SelectNextTop()
        {
            topOutfitIndex = (topOutfitIndex + 1) % 3;
            UpdateTopOutfit();
        }

        private void SelectPreviousBottom()
        {
            bottomOutfitIndex = (bottomOutfitIndex + 2) % 3;
            UpdateBottomOutfit();
        }

        private void SelectNextBottom()
        {
            bottomOutfitIndex = (bottomOutfitIndex + 1) % 3;
            UpdateBottomOutfit();
        }

        private void UpdateTopOutfit()
        {
            SetActiveOutfit(topOutfits, topOutfitIndex - 1);
            topOutfitName.text = topOutfitIndex == 0 ? "No top" : topOutfitIndex == 1 ? "Puffer jacket" : "RedFit dress V1 Skinned";
        }

        private void UpdateBottomOutfit()
        {
            SetActiveOutfit(bottomOutfits, bottomOutfitIndex - 1);
            bottomOutfitName.text = bottomOutfitIndex == 0 ? "No bottom" : bottomOutfitIndex == 1 ? "Skirt" : "Puffer pants";
        }

        private static void SetActiveOutfit(GameObject[] outfits, int selectedIndex)
        {
            for (int i = 0; i < outfits.Length; i++)
                outfits[i].SetActive(i == selectedIndex);
        }

        private void SwitchTo(string stateName, Button selectedButton)
        {
            paused = false;
            currentState = stateName;
            foreach (Animator target in animationTargets)
            {
                target.speed = 1f;
                target.CrossFadeInFixedTime(stateName, blendDuration, 0, 0f);
            }

            idleButton.RemoveFromClassList(ActiveMotionClass);
            walkingButton.RemoveFromClassList(ActiveMotionClass);
            actionButton.RemoveFromClassList(ActiveMotionClass);
            tPoseButton.RemoveFromClassList(ActiveMotionClass);
            selectedButton.AddToClassList(ActiveMotionClass);
            pauseButton.RemoveFromClassList(ActivePauseClass);
            pauseButton.text = "Pause";
            currentStateLabel.text = stateName;
            statusLabel.text = "PLAYING";
        }

        private void TogglePause()
        {
            paused = !paused;
            foreach (Animator target in animationTargets)
                target.speed = paused ? 0f : 1f;

            pauseButton.EnableInClassList(ActivePauseClass, paused);
            pauseButton.text = paused ? "Resume" : "Pause";
            statusLabel.text = paused ? "PAUSED" : "PLAYING";
            currentStateLabel.text = currentState;
        }

        private void ToggleCollapsed()
        {
            collapsed = !collapsed;
            previewContent.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            collapseButton.text = collapsed ? "⌄" : "⌃";
        }

        private void OnClothChanged(ChangeEvent<bool> change)
        {
            skirtCloth = bottomOutfits[0].GetComponentInChildren<SkirtClothController>(true);
            dressCloth = topOutfits[1].GetComponentInChildren<DressClothController>(true);
            skirtCloth.SetClothEnabled(change.newValue);
            dressCloth.SetClothEnabled(change.newValue);
        }

        private void OnBlendChanged(ChangeEvent<float> change)
        {
            blendDuration = change.newValue;
            UpdateBlendLabel();
        }

        private void UpdateBlendLabel()
        {
            blendValueLabel.text = $"{blendDuration:0.00} s";
        }
    }
}
