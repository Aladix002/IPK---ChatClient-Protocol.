# IPK Projekt 1 - DOKUMENTÁCIA

### Autor: Filip Botlo / xbotlo01

## Zhrnutie zadania 

Cieľom projektu bola implementácia klientskej aplikácie, ktorá komunikuje so serverom cez IPK25-CHAT protokol. Aplikácia môže na základe vybraných argumentov využívať transportný protokol TCP aj UDP.

## Ako sa pri riešení projektu využilo LLM

---

## Obsah
- [Teoretické pozadie UDP a TCP](#teoretické-pozadie)
- [Štruktúra aplikácie](#štruktúra-aplikácie)
- [Implementácia aplikácie](#implementácia-aplikácie)
- [Testovanie a overenie funkcionality](#testovanie-a-overenie-funkcionality)
- [Známe obmedzenia a nedostatky](#známe-obmedzenia-a-nedostatky)
- [Zdroje a použité materiály](#zdroje-a-použité-materiály)

---

## Teoretické pozadie UDP a TCP

#### TCP (Transmission Control Protocol)

#### UDP (User Datagram Protocol)


## Štruktúra aplikácie

Popis priečinkov, hlavných tried a ich úlohy. UML Diagram 

---

## Implementácia aplikácie

- Popis architektúry, moje triedy
- Riadenie stavov
- Správy a ich parsovanie a zostavovanie
- Spoľahlivý prenos pri UDP 

---

## Testovanie a overenie funkcionality

### Čo sa testovalo

- Správna inicializácia klienta cez CLI
- Odozva na príkazy užívateľa
- Správne správy na výstupe podľa špecifikácie
- Odchyt chýb a výpis 

### Prečo sa testovalo

- Overenie zhody so stavovým automatom
- Zaručenie spoľahlivého prenosu v UDP variante
- Dodržanie formátov výstupu (nutné pre automatizované testovanie)

### Ako sa testovalo

- Použitie študentských verejne dostupných automatických testov
- Referenčný server `anton5.fit.vutbr.cz`
- Testovanie rôznych príkazov a výstupov na CLI
- Sledovanie komunikácie v aplikácii Wireshark

### Testovacie prostredie

- OS: Ubuntu 22.04
- .NET: 8.0.3
- CPU: Intel i5, RAM: 16 GB
- Sieť: lokálne aj cez VPN (test NATu)
- Analyzované cez `Wireshark`

### Vstupy a výstupy



---

## Známe obmedzenia a nedostatky


---

## Zdroje a použité materiály









