/*
using System.Collections.Generic;
using DG.Tweening;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class CardSelectorParent : MonoBehaviour, IPointerExitHandler
{
    public CardSelector topCard;
    public CardSelector bottomCard;
    public List<CardSelector> cards;
    public Transform cardsParent;
    public Vector2 cardInterval;
    public Vector2 cardHighlightShift;
    public Vector2 cardHighlightPushShift;
    public Vector2 topCardChangeShift;
    public Vector2 bottomCardChangeShift;
    public float moveTime;
    public float delayTime;
    public bool interactable = true;
    public int currentSlot;

    public GearManagerViewModel viewmodel;
    public UnityEvent OnTopCardChange;
    public void SetCurrentSlot(int set) => currentSlot = set;

    private readonly List<Sequence> sequences = new();
    private void CompleteAllTweens()
    {
        for (int i = 0; i < sequences.Count; i++)
        {
            sequences[i].Complete();
        }
        sequences.Clear();
    }

    public void Start()
    {
        cards = new(GameManager.instance.gearManager.PhysicalMaxCardCount);
        CardSelector[] allCards = cardsParent.GetComponentsInChildren<CardSelector>(true);
        for (int i = 0; i < allCards.Length; i++)
        {
            allCards[i].PointerClickEvent.AddListener(OnCardOfIndexClick);
            allCards[i].PointerEnterEvent.AddListener(OnCardOfIndexEnter);
            allCards[i].cardData.Initialize();
        }
        bottomCard.cardData.Initialize();
        topCard.cardData.Initialize();
        topCard.index = 0;

        topCard.PointerEnterEvent.AddListener(OnTopCardEnter);
        topCard.SetCardData(allCards[0].cardData, 0);

        viewmodel = new(GameManager.instance.gearManager);
        viewmodel.CardList.Subscribe(list => UpdateCardDatas(list)).AddTo(this);
    }

    public void SpreadCards(int index)
    {
        CompleteAllTweens();
        // SetUpdate(true): 카드 UI는 TimeManager가 게임을 멈춘 동안에도 움직여야 한다.
        // 시퀀스에 걸면 안에 넣은 트윈들에도 함께 적용된다.
        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        for (int i = 0; i < cards.Count; i++)
        {
            sequence.Insert(delayTime * (1 + index - i < 0 ? 0 : 1 + index - i),
                cards[i].transform.DOLocalMove(
                cardInterval * (cards.Count - i)
                + (i < index && index < cards.Count ? cardHighlightPushShift : Vector2.zero)
                + (i == index ? cardHighlightShift : Vector2.zero),
                moveTime));
        }
        sequence.Play();
        sequences.Add(sequence);
    }

    public void CollectCards()
    {
        CompleteAllTweens();
        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        for (int i = 0; i < cards.Count; i++)
        {
            sequence.Insert(delayTime * (cards.Count - i), cards[i].transform.DOLocalMove(Vector2.zero, moveTime));
        }
        sequence.Play();
        sequences.Add(sequence);
    }

    public void UpdateCardDatas(List<CardData> gearManager)
    {
        List<CardData> cardDatas = gearManager.playerCardEquipData;
        CardSelector[] allCards = cardsParent.GetComponentsInChildren<CardSelector>(true);
        cards.Clear();
        int cardMax = gearManager.MaxCardCount;
        int slotMax = gearManager.MaxGearSlotCount;
        for (int i = 0; i < allCards.Length; i++)
        {
            CardSelector card = allCards[i];
            card.gameObject.SetActive(false);
            card.SetSlotCount(slotMax);
            if (i < cardMax)
            {
                card.SetCardData(cardDatas[i], i);
                card.gameObject.SetActive(true);
                cards.Add(card);
            }
        }
        bottomCard.SetSlotCount(slotMax);

        if (cardMax == 0)
        {
            topCard.SetSlotCount(0);
            topCard.SetCardData(null, topCard.index);
        }
        else
        {
            topCard.SetSlotCount(slotMax);
            topCard.SetCardData(cards[topCard.index].cardData, topCard.index);
        }
    }

    public void ChangeTopCard(int index)
    {
        if (index == topCard.index) return;
        if (index >= cards.Count) index = cards.Count - 1;
        if (index < 0) index = 0;
        float delay = cards.Count * delayTime;
        SetInteractable(false);
        bottomCard.SetCardData(cards[index].cardData, index);
        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Insert(delay, topCard.transform.DOLocalMove(topCardChangeShift, moveTime));
        sequence.Insert(delay, bottomCard.transform.DOLocalMove(bottomCardChangeShift, moveTime));
        delay += moveTime;
        sequence.InsertCallback(delay, () => bottomCard.SetCardData(topCard.cardData, index));
        sequence.InsertCallback(delay, () => topCard.SetCardData(cards[index].cardData, index));
        sequence.Insert(delay, topCard.transform.DOLocalMove(bottomCardChangeShift, 0));
        sequence.Insert(delay, bottomCard.transform.DOLocalMove(topCardChangeShift, 0));
        delay += 0.0001f;
        sequence.Insert(delay, topCard.transform.DOLocalMove(Vector2.zero, moveTime));
        sequence.Insert(delay, bottomCard.transform.DOLocalMove(Vector2.zero, moveTime));
        sequence.OnComplete(() => { SetInteractable(true); OnTopCardChange?.Invoke(); });
        sequence.Play();
        sequences.Add(sequence);
    }

    public void SetGearData(Gear gear)
    {
        viewmodel.SetCurrentEdittingCard(topCard.index);
        viewmodel.SetGearData(currentSlot, gear);
    }

    public void SetInteractable(bool set)
    {
        interactable = set;
        if (set == false) CollectCards();
    }

    public void OnTopCardEnter(int temp)
    {
        if (interactable)
            SpreadCards(cards.Count);
    }
    public void OnCardOfIndexClick(int index)
    {
        if (interactable)
            ChangeTopCard(index);
    }

    public void OnCardOfIndexEnter(int index)
    {
        if (interactable)
            SpreadCards(index);
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        if (interactable)
            CollectCards();
    }
}
*/