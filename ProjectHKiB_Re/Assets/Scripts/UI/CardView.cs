using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardView : MonoBehaviour
{
    public GraffitiPatternView[] patternView;
    public Image[] gearImage;
    public TextMeshProUGUI cardName;

    public bool keepEmptyPattern;

    public void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        for (int i = 0; i < patternView.Length; i++)
            patternView[i].Initialize();

        for (int i = 0; i < gearImage.Length; i++)
            gearImage[i].enabled = false;
        if (cardName) cardName.text = null;
    }

    public void UpdateCard(int cardIndex) => UpdateCard(GameManager.instance.gearManager.GetCardData(cardIndex));

    public void UpdateCard(Card card)
    {
        if (card == null) Initialize();

        for (int i = 0; i < patternView.Length; i++)
        {
            patternView[i].gameObject.SetActive(true);
            if (card.GearList[0].data && i < card.GearList[0].data.graffitiCodes.Count)
            {
                patternView[i].UpdatePattern(card.GearList[0].data.graffitiCodes[i]);
            }
            else
            {
                if (keepEmptyPattern) patternView[i].Initialize();
                else patternView[i].gameObject.SetActive(false);
            }
        }

        for (int i = 0; i < gearImage.Length; i++)
        {
            gearImage[i].enabled = true;
            if (i < card.GearList.Length && gearImage[i] != null && card.GearList[i].data != null && card.GearList[i].data.itemIcon != null)
                gearImage[i].sprite = card.GearList[i].data.itemIcon;
            else
                gearImage[i].enabled = false;
        }

        if (cardName) cardName.text = card.cardName;
    }
}