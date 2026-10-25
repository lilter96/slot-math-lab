# Недостающие примитивы и возможности языка графов

Исследование от 23 октября 2026 года для текущих пяти семейств игр и 15 полных
конфигураций. Исходный код: `8593ab9d5bc16edd81d2fd6c40246908bbdcf916`.
Это предложения контрактов, а не реализованные расширения ядра. Новые атомы
требуют отдельного согласования по `AGENTS.md`. Текущая реализация упрощения
выражений описана в `docs/GRAPH_SIMPLIFICATION.md` и PR #23.

Наиболее полезный первый набор: общие таблицы распределений, упорядоченный
пакетный Draw, локальные типизированные значения и композиция коллекций.
Следом стоит проверять ограниченный обход произвольной связности и операции
scan/компактации. Компилятору нужен анализ зависимостей, эффектов и времени
жизни значений; без него новые операции будут компактнее записываться, но могут
сохранить лишние проходы, записи и копирования.

Сокращение исходника, сокращение раскрытого графа и ускорение симуляции — три
разных результата. Приведённые ниже сокращения количества элементов проверены
по исходным JSON. Для предложенных примитивов прирост rounds/s ещё не измерен.

**Что показал текущий корпус.** Под «сохранёнными узлами» понимаются узлы корня
плюс узлы каждой сохранённой custom definition ровно один раз. «Раскрытые узлы»
— число примитивных узлов при рекурсивной замене каждого Library его телом;
динамические итерации циклов в это число не входят. AST считается по сохранённым
выражениям и выражениям портов, без повторного раскрытия ссылок. Размер JSON
приведён без отступов, в KiB.

| Конфигурация | Определений | Сохранённых узлов | Раскрытых узлов | Элементов AST | JSON KiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Ancient Dragon | 7 | 72 | 406 | 332 | 58,0 |
| Nine Lives | 21 | 225 | 413 | 1384 | 190,1 |
| Nine Lives buy | 19 | 184 | 227 | 1199 | 155,9 |
| Woodland Whisper | 10 | 89 | 486 | 368 | 66,9 |
| Woodland Whisper buy | 12 | 120 | 496 | 575 | 100,4 |
| X7 Club | 10 | 148 | 379 | 718 | 111,8 |
| X7 Club buy | 5 | 78 | 200 | 423 | 59,8 |
| Le Militare spin | 38 | 254 | 430 | 1915 | 293,7 |
| Le Militare standard | 36 | 236 | 412 | 1871 | 284,6 |
| Le Militare super | 36 | 236 | 412 | 1871 | 285,2 |
| Le Militare elite | 36 | 236 | 412 | 1871 | 284,9 |
| Le Militare chance | 38 | 256 | 432 | 1929 | 296,1 |
| Le Militare air raid | 38 | 252 | 428 | 1912 | 293,1 |
| Le Militare siege spin | 38 | 254 | 430 | 1915 | 293,8 |
| Le Militare recon spin | 38 | 254 | 430 | 1915 | 293,7 |

Статические размеры не определяют число выполняемых операций: условная ветка
может почти никогда не выбираться, а один небольшой цикл — запускаться много
раз. В сохранённом JSON размер уже уменьшен переиспользованием подграфов.

Фактические счётчики из парных замеров PR #23:

| Полный настроенный раунд | Вызовов графовых циклов/раунд | Итераций/раунд | Аллокации B/раунд |
| --- | ---: | ---: | ---: |
| Nine Lives paid | 5,61 | 13,15 | 53,9 |
| Ancient Dragon paid | 0,01 | 0,08 | 33,4 |
| Woodland Whisper paid | 0,02 | 0,13 | 27,5 |
| X7 Club paid | 0,07 | 0,29 | 68,9 |
| Le Militare paid | 10,20 | 19,47 | 324,2 |
| Le Militare standard bonus entry | 180,99 | 384,47 | 42727,3 |

Бонусный вход Le Militare включает в среднем 9,10 free spins в этом корпусе
замеров; его нельзя сравнивать с одним платным спином. Счётчики — точные итоги
этой выборки, а не доли CPU. Малые значения циклов у линейных игр объясняются
в том числе статическим размещением каждой линии вместо графового цикла.

**Что уже существует и не должно выдаваться за новую идею.**

- Map, Filter, Fold, Aggregate, индекс элемента и индексная переменная внутри
  итерации уже есть. Обычный gather выражается Map плюс index.
- Шесть операций над конечными множествами уже работают с точными 64-битными
  масками. В символьных подграфах уже используются битовые плоскости.
- В `SamplingSubexpressions` есть ленивое кэширование повторных чистых
  подвыражений внутри одного элемента Map/Filter/Fold. Кэш сбрасывается для
  следующего элемента. Это не глобальный CSE между изменениями состояния.
- `SamplingCodegen` уже объединяет цепочку append в одну сборку массива и
  переиспользует сгенерированный код одинаковой формы через `SamplingShapes`.
- Последовательности Draw/Modify уже компилируются совместно; запись поля
  самому себе имеет облегчённый Touch. Массивы повторно используются между
  раундами в ограниченной arena, когда это допускают измерения и persistence.
- Для небольших колонок gravity уже использует таблицу исходных строк;
  для высоких — общий Filter. Это сильный существующий baseline.
- NTT/CRT, марковские операторы, моменты высших порядков и факторизованные
  операторы уже реализованы в соответствующих математических подсистемах.
  Их нельзя заново объявлять недостающими примитивами симуляции.

Исходные точки проверки: `Model/Expression.cs`, `Compiler/SamplingSubexpressions.cs`,
`Compiler/SamplingCodegen.cs`, `Compiler/SamplingFrame.cs`,
`frontend/src/games/shared/graph/mechanics/`, `docs/MATH_ENGINE_ROADMAP.md`.

**Приоритеты предложений.** P0 — первый пакет контрактов; P1 — после него;
P2 — отдельный эксперимент после измерения подходящей нагрузки. Это порядок
исследования и реализации, а не уже согласованный объём расширения ядра.

| Приоритет | Возможность | Основной эффект | Где реализовывать |
| --- | --- | --- | --- |
| P0 | Неизменяемые Distribution assets и выбор семейства по индексу | Общие веса, меньше JSON и подготовки, корректные state-dependent distributions | Общая модель данных и контракт Draw |
| P0 | Ordered DrawMany | Убирает россыпь скалярных Draw и последующую сборку массива | Общий атом со скалярным эталоном |
| P0 | Let, типизированный результат из нескольких значений, collection input как выражение | Меньше дублирования AST и временных state keys | Язык выражений, типизация, lowering |
| P0 | Read/write/error/RNG/observation effects и lifetime analysis | Делает безопасными дальнейшее устранение работы и повторное использование памяти | Общий IR компилятора |
| P1 | Ограниченный Reachable для произвольной связности | Компактный обход вместо ручного графового управления frontier | Чистая общая операция |
| P1 | Scan и segmented compaction | Один план перестановки для symbols и всех переносимых атрибутов | Общие операции коллекций |
| P1 | Repeat over collection с одним редактируемым телом | Кратное сокращение раскрытого представления линий | Сначала редактор и JSON-композиция существующих атомов |
| P1 | Bounded foldWhile, any/all | Явная остановка прохода после достижения результата | Общее выражение с ограничением работы |
| P2 | Indexed reduction/partition indices и типизированный scatter | Счётчики и локальные изменения без лишних обходов | Сначала композиция; отдельный атом только по результатам замеров |
| P2 | Ограниченная предтабуляция чистых функций | Возможна смена вычисления на lookup | Общая стратегия компилятора |
| P2 | Условные произведения распределений с аддитивным ограничением | Избежать экспоненциального списка комбинаций | Генератор математических данных и явный контракт вероятностей |
| P2 | Векторное исполнение независимых логических streams | Потенциал SIMD между потоками | Новый backend исполнения, не атом механики |

**1. Distribution assets и действительно динамический categorical draw.**

В сохранённых определениях Nine Lives находится 91 скалярный Draw с четырьмя
уникальными inline-таблицами. Сами повторённые `drawWeights` занимают 26435
байт; уникальные таблицы — 1118 байт. У X7 Club — 15760 против 868 байт,
у Le Militare spin — 77632 против 26516 байт. Это верхняя оценка экономии
сырых payloads при вынесении таблиц; идентификаторы, ссылки и описания
новых assets тоже займут место. Для всего JSON экономия будет меньше, чем
отношение размеров одних таблиц.

Контракт: immutable ordered outcomes и точные неотрицательные веса с
положительной суммой; стабильный ID и fingerprint всего содержимого.
Draw ссылается на таблицу или выбирает одну из конечного семейства по
целочисленному индексу. Aliases готовятся один раз на таблицу, имеют ту же
FIFO-политику построения и тот же порядок исходов. Общие assets принадлежат
проекту/конфигурации; редактирование общей таблицы показывает все затронутые
использования и создаёт новую идентичность вычисляемого конфига.

Нельзя сортировать исходы, удалять нулевые веса или менять alias pairing под
видом экономии: это может изменить воспроизведение при том же seed. Можно
разделять неизменяемые данные и готовые таблицы с одинаковым полным контрактом.

Важный существующий пробел: `DrawNode.WeightExpressionId` объявлен, но
`ExactExpressionEvaluator.EvaluateAsWeights` сейчас принимает Number и
возвращает одноэлементный WeightSet. Массив динамических весов этим не
выражается. Кроме того, `SamplingPlan.Wrap` отказывается от compiled plan
при наличии WeightExpressionId. Ограничение отмечено и в
`frontend/unit/fixtures/pr19AuthoringProofs.ts`.

Предлагаются два отдельных уровня: быстрый выбор из конечного immutable
семейства и отдельно полноценный вектор произвольных state-dependent weights.
Второй требует проверки длины, точности, отрицательных/нулевых значений и
стоимости построения распределения. Нельзя смешивать эти режимы или молча
считать mutable weights константными. Для дизайнера это таблица вариантов
и явный выбор строки по состоянию вместо дерева отдельных Draw-веток.

Ещё одно полезное свойство общего контракта — тип результата. `Flow.draw`
сейчас записывает outcomeId как строку, а numeric Value выставляет в 0;
авторские числовые идентификаторы затем преобразуются через toNumber.
Типизированный исход Number/String позволяет хранить числовые данные прямо
числами. При этом значение данных нельзя автоматически масштабировать как
денежную выплату: legacy value-channel и новые typed data outcomes требуют
явного разграничения в контракте и теста с дробными payout units.

**2. Ordered DrawMany.**

В Nine Lives `paid-board` и `free-board` содержат по 30 Draw и один Map,
собирающий `cell1`…`cell30` через дерево choose. `coin-prizes` содержит ещё
30 Draw плюс два вычислительных узла. В X7 Club `board` и `coin-prizes`
содержат по 15 Draw; `respin` — 30 чередующихся розыгрышей двух типов.
Отдельный `paid-board` из 31 узла можно выразить одним batch draw, сразу
возвращающим массив. Это сокращение конкретного подграфа, не 31-кратное
ускорение игры.

Минимальный контракт: ограниченное количество выборок из одной таблицы,
запись результата в массив, возрастающий порядок элементов. Эталонная
семантика — буквальный вызов существующего скалярного Draw для каждого
элемента. Для смешанной последовательности X7 потребуется отдельное
расширение: явное расписание ID таблиц с однородным типом результата.
Чередование land/prize должно остаться прежним; две независимые batch-команды
«сначала все land, потом все prize» неэквивалентны по RNG.

Первые выигрыши — удаление промежуточных полей и choose, меньше связей и
повторённых описаний. Число случайных выборок и работа alias остаются.
Эффект ядра нужно сравнивать с уже скомпилированной цепочкой Draw.
Пропускать розыгрыш на удержанной/неиспользуемой ячейке нельзя, если исходный
граф его выполнял.

Автоматическая миграция допустима только с анализом потребителей временных
полей и точек наблюдения. Сохраняемые пользователем `cell1` или измерение
между двумя Draw нельзя потерять. Режим совместимости должен сохранить
такие выходы, либо преобразование явно показывает изменение схемы и
отказывается от обещания полного совпадения состояния. Это не runtime
распознавание игры или специального authored pattern.

**3. Локальные значения, несколько результатов и композиция коллекций.**

Текущие Map/Filter/Fold принимают `StateKey`, а не произвольное выражение
коллекции. Их тела ограничены одним уровнем итерации. Есть чтение record из
состояния, но отсутствует отдельный AST-конструктор record/tuple и локальный
Let. Поэтому автор вынужден повторять выражение или писать результат в
состояние дополнительным Modify.

Предлагаемый Let связывает чистое значение в лексической области без записи
в глобальное состояние. Вычисление правой части выполняется один раз, в
определённом месте и порядке. Неиспользуемый eager Let нельзя удалять, если
его правая часть может завершаться ошибкой. Тело сохраняет правильное
затенение переменных; чтение поля и локальное имя различаются явно.

Типизированные tuple/record позволяют одному проходу вернуть несколько
связанных результатов. Начать стоит с фиксированного небольшого набора
полей и статической проекции. Compiled lowering может хранить такие
результаты в локальных регистрах/буферах, но эталонное исполнение и экспорт
должны сохранять обычные неизменяемые значения.

Collection input как выражение позволяет записать Map(Filter(xs, p), f)
без промежуточного state key. Это основание для общего fusion. При этом
слияние eager-проходов способно изменить порядок ошибок: исходный Map
завершает вычисление всего промежуточного массива до запуска следующего
Map. Перестановка допустима после доказательства тотальности и отсутствия
наблюдаемых эффектов либо сохраняет исходный порядок.

В Le Militare combat несколько проходов отдельно строят held, board, armed
и cellCounts. Несколько результатов и анализ зависимостей дают возможность
сократить часть проходов, сохраняя различие между старым и новым board.
Нельзя объединять всё механически: armed имеет длину числа колонок, а board
— числа ячеек, и некоторые вычисления зависят от предыдущей записи.

Модель fusion как средства сохранения модульности при устранении промежуточных
массивов хорошо описана авторами [Futhark](https://www.futhark-lang.org/blog/2026-03-24-scan-scatter-fusion.html).
Применимость и эффект именно для наших маленьких массивов требуют замера.

**4. Reachable по произвольной связности.**

Предлагаемый контракт — чистое ограниченное достижимое множество:
`reachable(adjacency, allowed, seeds)`. Геометрия задаётся данными; операция
ничего не знает о слотах, символах, Wild или выплатах. Для первого варианта
достаточно явного домена до 64 вершин и массива масок соседей. Направленность
следует входным рёбрам. Семена вне allowed либо отклоняются, либо явно
пересекаются с allowed; выбрать нужно один документированный вариант.

Результат — наименьшее множество, содержащее разрешённые семена и замкнутое
по разрешённым рёбрам. Завершение следует из монотонного добавления не более
V вершин; это не эвристика сходимости. Реализация может использовать очередь
O(V+E), либо word-level frontier для малого домена. Нужны ограничения работы
и отмена. Произвольные циклы входной связности допустимы.

Здесь есть три разных графа: авторская программа с явными Loop,
связность элементов данных и марковский граф переходов между состояниями
игры. Reachable работает со вторым. Матрицы марковских наград относятся к
третьему и уже используются отдельной exact-подсистемой; перенос результатов
одного анализа на другой требует самостоятельного обоснования.

Текущие `grow-group`/`pay-group` вручную ведут reached и group, делают два
раскрытых шага и затем графовый Loop. Чистое Reachable заменило бы внутреннее
управление ростом. Внешний порядок символов и групп, исключение уже занятых
Wild, вес ячейки и paytable остаются редактируемыми JSON-подграфами.

Нельзя заменить всю обработку всех символов одним обычным connected-components:
Wild, доступный двум символам, может быть забран первым оплачиваемым кластером.
Такое решение зависит от авторского порядка и является частью игры.
Общая алгебра связности отделяется от правил награды, как в
[GraphBLAS, включая примеры BFS](https://graphblas.org/docs/GraphBLAS_API_C_v2.0.0.pdf).
Это аргумент в пользу общего контракта, а не предложение подключать тяжёлую
матричную библиотеку ради 30 вершин.

**5. Scan и segmented compaction.**

Scan возвращает префиксные накопления; inclusive и exclusive варианты должны
иметь раздельную семантику. Начать можно с точной суммы целых и булевых флагов,
затем обобщать на явно допустимые операции. Перегруппировка требует
ассоциативности и не должна менять ошибки.

Стабильная компактация по сегментам принимает границы сегментов и keep-mask,
возвращает source-index plan и число свободных мест в каждом сегменте.
Порядок выживших элементов сохраняется. Один план используется для symbols,
held, множителей, здоровья или любого другого массива атрибутов той же длины.
Заполнение свободных мест и розыгрыши выполняются отдельными обычными атомами
в авторском порядке. «Gravity» остаётся удобным редактируемым подграфом.

Текущая таблица `settledSources` содержит r·2^r элементов: для пяти строк
это 160, для восьми — 2048. Выше восьми генератор уже использует общий Filter,
поэтому утверждение, что текущая реализация экспоненциальна при любых r,
было бы неправильным. Scan/compaction дают более единый контракт и уменьшают
дублирование переноса атрибутов; на текущих пяти строках LUT может остаться
быстрее нового варианта. Обычный gather сначала оставить Map + index.

Scan, histogram, map с несколькими входами и scatter являются стандартными
общими операциями в [Futhark SOACs](https://futhark-lang.org/docs/prelude/doc/prelude/soacs.html).
Полезность префиксных сумм как строительного блока изложена в
[работе Blelloch](https://www.cs.cmu.edu/afs/cs.cmu.edu/project/scandal/public/papers/CMU-CS-90-190.html).

**6. Одно тело для коллекции линий и других повторений.**

Ancient Dragon сохраняет 25 размещений семиузлового payline; с учётом
платного и free-spin путей раскрывается 50 тел, то есть 350 примитивных узлов
только этих тел. Woodland Whisper раскрывает 60 тел — 420 узлов; X7 Club —
20 тел, или 140 узлов. Один data-driven цикл хранит одно тело для каждого
места вызова, плюс служебные узлы привязки текущего элемента и выхода.
Это потенциально кратное сокращение раскрытого представления. Не следует
переносить эти отношения на размер сохранённого JSON или rounds/s.

Первый вариант — визуальный «Для каждого элемента» с таблицей путей линий,
который создаёт существующий Loop, текущий индекс и индексный доступ к данным.
Пользователь меняет число/форму линий таблицей. Новый ForEach в ядре не нужен,
пока не доказано, что existing Loop + bindings не удовлетворяет контракту.
Затем можно отдельно предложить runtime-параметры reusable subgraph, если
число промежуточных записей и сложность привязок остаются проблемой.

Такой цикл может проиграть текущему развёрнутому варианту: появляется
управление итерацией, а `SamplingShapes` уже разделяет машинный код одинаковой
формы. Система должна позволять компактное авторское представление и
разворачивание на уровне общего компилятора там, где это выгодно.

**7. Fold с явной остановкой и индексные операции.**

Bounded foldWhile отличается от обычного Fold тем, что проверяет заданное
условие перед очередным элементом и прекращает чтение оставшихся элементов.
Нужны определённые случаи пустого массива, первого false и ошибки в ещё не
достигнутом элементе. Это полезно для префикса совпадений, достижения лимита
и поиска первого подходящего элемента. Any/all — частные удобные операции;
их сначала можно выразить через общий короткий Fold.

Для scatter нужно заранее определить дубликаты индексов: ошибка, last-write
в исходном порядке или reduction. Недетерминированная запись недопустима.
Для immutable входа копирование всего массива всё ещё O(n); стоимость O(k)
достижима при допустимом повторном использовании буфера, а не от одного
названия scatter. При обновлении одной позиции в массиве из 15 элементов
новый интерфейс может оказаться полезнее, чем прирост скорости.

PartitionIndices/ReduceByIndex могли бы построить masks/counts по метке за один
проход. Однако текущий `read-board` уже использует битовые плоскости и один
проход, а число символов мало. Сначала проверить композицию через typed
accumulator и сравнить оба представления; не добавлять отдельную операцию
с заведомо необоснованным обещанием ускорения.

**8. Основание умного оптимизатора: эффекты, области и память.**

Для каждой общей операции нужны явные свойства: читаемые поля, записи,
лексические привязки, вероятность ошибки/условия тотальности, RNG, наблюдения,
отмена и возвращаемая структура. Затем IR может вести версии состояния и
доказать, где значение неизменно, какой промежуточный результат мёртв и какой
буфер больше никому не доступен. Имена игр и механик в этих решениях не участвуют.

Это позволит расширять CSE за пределы одного selector, выносить допустимые
инварианты, уменьшать промежуточные состояния и переиспользовать память внутри
длинного раунда. Но нельзя выносить потенциально ошибочную операцию из ветки,
переставлять Draw, пропускать наблюдение или портить snapshot состояния.
Внешний override обязан отменять неактуальные доказательства.

Полезные ориентиры — разделение эффектов и допустимости спекуляции в
[MLIR](https://mlir.llvm.org/docs/Rationale/SideEffectsAndSpeculation/) и анализ
alias/use-def перед повторным использованием буфера в
[MLIR Bufferization](https://mlir.llvm.org/docs/Bufferization/).
Заимствуется дисциплина анализа; переписывать существующее .NET-ядро на LLVM
для этого исследования не требуется.

В статическом корпусе 24/225 узлов Nine Lives и 42/254 Le Militare spin —
записи, которые условно накапливают trace. Это не доля CPU и не готовая
экономия: trace=false уже упрощается, а записи самому себе уже имеют Touch.
Полностью убирать их можно только при сохранении проверок, наблюдений,
экспортируемого состояния и возможностей внешнего override.

**9. Предтабуляция конечных чистых функций.**

Если у чистого блока явно конечные небольшие домены входов и константные
параметры, компилятор может перечислить все входы и построить точную таблицу.
Сохраняется исходное редактируемое тело и его самостоятельное эталонное
исполнение. Admission зависит от размера домена, размера результата,
стоимости построения и ожидаемого числа запусков. Идентичность таблицы
включает тело, параметры, типы и порядок домена.

Например, пять входов по восемь значений дают 32768 строк. При 16 байтах
результата это 512 KiB без служебных данных. Пять входов по 14 значений дают
537824 строки и около 8,2 MiB. Больше строк не означает более быстрый поиск:
таблица может ухудшить cache locality. Ошибки должны сохранять точную
семантику; нельзя превратить недопустимый вход в нулевой выигрыш.

Это общая стратегия для явно конечной чистой функции, а не распознавание
«вот граф paylines». Она способна дать большую локальную экономию на сложном
маленьком домене; для текущего короткого дерева из пяти проверок таблица может
оказаться хуже. Сначала нужен маленький независимый эксперимент и holdouts.

**10. Условные произведения и пакетное исполнение streams.**

`showingStops` сейчас перечисляет комбинации количеств показанного символа
по барабанам. Общий случай независимых конечных величин с аддитивным
ограничением можно готовить через suffix-DP, затем выбирать условные исходы.
Для R величин, диапазона суммы K и A исходов типичная подготовка O(RKA)
избегает перебора A^R комбинаций. Это предложение ограниченного математического
конструктора, а не универсального «сэмплировать любое условие».

Другой порядок условных розыгрышей меняет RNG transcript относительно
нынешнего выбора pattern и последующих stops. Совпадение совместного закона
не доказывает воспроизведение при том же seed. Новый явно авторский граф должен
иметь новую идентичность; старые сохранённые запуски продолжают использовать
старый граф. Сначала полезнее общий контракт семейства распределений.

SIMD между несколькими логическими streams — отдельный backend. Текущий
контракт использует xoshiro256**, chunks по 65536 и упорядоченное слияние.
Нельзя просто назначить независимый seed каждому спину: число RNG-вызовов
в предыдущем раунде влияет на следующий раунд того же stream. Возможный
эксперимент группирует существующие независимые chunks, сохраняет порядок
внутри каждого stream, rejection sampling и порядок статистического merge.
Сложные бонусы вызывают расхождение ветвей; exact rationals и BigInteger
требуют скалярных fallback. SIMD не является первым недостающим атомом.
Microsoft также рекомендует подтверждать выгоду конкретной SIMD-операции
[измерением](https://learn.microsoft.com/dotnet/standard/simd).

**Отдельный предел точности при авторстве масок.**

Backend finite-set уже допускает точные числа до 2^64−1. Но `cellSet` в
`frontend/src/games/shared/graph/expr.ts` суммирует `2 ** cell` через number,
затем делает строковый integer literal. Проверка того же вычисления в Node 22:

| Полное множество | Текущий literal | Точное значение |
| --- | --- | --- |
| 30 ячеек | 1073741823 | 1073741823 |
| 53 ячейки | 9007199254740991 | 9007199254740991 |
| 54 ячейки | 18014398509481984 | 18014398509481983 |
| 64 ячейки | 18446744073709552000 | 18446744073709551615 |

Текущие полные игры используют меньшие поля. Исправление авторских литералов
через BigInt/decimal strings — сначала обеспечение уже заявленной 64-битной
возможности. Для полей больше 64 требуется отдельно согласованный
`FiniteSet(width)` с ограниченным multiword представлением и однословным
быстрым вариантом для малых ширин. Это расширение возможностей, а не
обещание ускорить существующие 30-ячеечные поля.

**Оценка возможных ускорений.** Для неизменной нагрузки, если доля времени p
ускоряется в k раз, общий коэффициент равен `1 / ((1-p) + p/k)`.

| Условие эксперимента, не измеренная доля текущей игры | Общий коэффициент |
| --- | ---: |
| 20% времени ускорены в 5 раз | 1,19× |
| 40% времени ускорены в 5 раз | 1,47× |
| 60% времени ускорены в 4 раза | 1,82× |
| 70% времени ускорены в 5 раз | 2,27× |
| 80% времени ускорены в 8 раз | 3,33× |

Для 2× нужно устранить половину исходного времени или эквивалентно ускорить
достаточно большую часть. Для 10× даже бесконечное ускорение выбранной части
требует охватить минимум 90% исходного времени. Нельзя складывать коэффициенты
новых операций, затрагивающих одну и ту же работу.

В текущем baseline PR #23 получены +4,31% Ancient Dragon, +5,11% Woodland
Whisper, +0,45% Nine Lives одним worker и +1,15% восемью; X7 Club и два
варианта Le Militare — от −0,31% до −0,88%. В замерах длительность GC pauses
составила примерно 0,01–0,07% для платных раундов и 0,24% для Le Militare bonus.
Последний аллоцирует около 260 MB/s, однако устранение только пауз GC не даёт
кратного ускорения. Уменьшение вычислений и копирований следует мерить отдельно.

Необходимо снять свежий профиль после выбора первого прототипа. Здесь нет
новой оценки долей CPU для каждого предлагаемого атома. Существующие native
профили в `docs/verification/TGSLOTS_OPTIMIZATION.md` относятся к предыдущим
вариантам нагрузки и не заменяют такой замер. EventPipe safe-point samples
уже давали вводящую в заблуждение атрибуцию PollGC в этом проекте.

Выполнен отдельный исследовательский контроль на существующих атомах:
окно Ancient Dragon переписано через вложенный index по массиву полос и Map
позиций вместо явной сборки каждого элемента. Выражение окна сократилось
со 106 до 24 элементов AST, добавилось 11 элементов для сборки остановок —
35 вместо 106 в этом фрагменте. Код ядра не менялся.

| Вариант | Медиана полных раундов/с | Диапазон сохранённых повторений |
| --- | ---: | ---: |
| Исходная сборка, общие дополнительные immutable данные контроля | 305285 | 301581–309452 |
| Компактный Map/index и дополнительный scratch-массив остановок | 288714 | 287063–290454 |

Наблюдаемое изменение −5,43%. Один worker на CPU 2, тот же frozen Release
binary и compiled sampler, порядок ABBA, 524288 раундов, 131072 warmup,
три timed repeats на процесс с отбрасыванием первого. Сравнены 12 полных
статистических выходов: count, RTP, variance, hit rate, max, cap, histogram,
loop evidence совпали. Candidate добавляет scratch state; полного тождества
схем состояния этот эксперимент не заявляет. Текущие конфиги не мигрированы.

Этот отрицательный контроль подтверждает необходимость модели стоимости.
Дополнительная индексация и промежуточный массив — различия реализации,
а их отдельный вклад пока не измерен. Дальнейший общий компилятор может
рассматривать разворачивание малого Map с известными позициями и устранение
допустимых промежуточных значений. Компактный editable source и оптимальная
машинная форма могут отличаться. Raw inputs, скрипт и результаты сохранены
в `/tmp/graph-refactor-research/window-normalization/` и
`/tmp/graph-refactor-research/window_normalization.py`.

**Как это должно выглядеть для слот-дизайнера.**

На верхнем уровне остаются понятные редактируемые подграфы Spin, Cluster Pays,
Cascade, Hold & Spin. Каждый раскрывается до общих атомов и выражений. Для
создания поля пользователь выбирает таблицу распределения и размер массива;
для линий редактирует таблицу путей и одно общее тело; для каскада — сегменты,
маску удаления и связанные массивы атрибутов. Правила Wild, cap и завершения
остаются видимыми параметрами и выражениями JSON.

Предпросмотр показывает изменение исходника, затронутые shared definitions,
схему состояния, RNG и ссылки измерений. Для производительности нужны отдельные
измеренные поля: время компиляции, rounds/s, bytes/round и режим исполнения.
Оценка AST не заменяет timing. Редактор может предлагать общую библиотечную
композицию, но ядро не выводит поведение по имени подграфа или типу игры.

**Порядок дальнейшей работы и критерии принятия.**

1. Зафиксировать контракты Distribution asset + семейство + Ordered DrawMany.
   Первый prototype сравнивается с обычной уже compiled цепочкой скалярных
   Draw, включая случай, когда старые временные поля нужны наблюдателям.
2. Отдельно утвердить Let и типизированные локальные результаты; затем
   expression-valued collection inputs и анализ эффектов. Измерять устранение
   проходов и копирований, а не только уменьшение JSON.
3. Сделать дизайнерские repeat/табличные формы на существующих атомах.
   Компактный исходник и быстрый backend могут иметь разные представления.
4. Сравнить Reachable с текущими bitset-подграфами и segmented compaction с
   нынешними LUT/Filter. Начать с математически независимых примеров, затем
   переносить механики в editable JSON.
5. Предтабуляцию, conditional-product DP и SIMD проверять как отдельные
   ограниченные эксперименты с отрицательными контролями и holdout-графами.

Для нового атома обязательны: эталонная семантика, типы и shape, правила ошибок,
ограничение работы/памяти, отмена, точность, независимые тесты и согласованное
поведение exact/compiled/reference. Если structured exact не поддерживает
сжатое представление, он обязан явно использовать корректный бюджетный
fallback, не заявляя недоказанный Exact.

Регрессии для Draw покрывают uniform/nonuniform/zero weights, исходный порядок,
полный RNG transcript и последующий независимый Draw. Для связности — отдельный
BFS oracle, циклы, разрывы, направленные рёбра, пустые множества и Wild sharing
на уровне JSON-механики. Для компактации — все маски маленького сегмента,
ragged/пустые сегменты, перенос нескольких атрибутов и независимость snapshots.
Для Let/fusion — затенение, ошибки в разных элементах и ветках, overrides,
наблюдения и сохранение старых версий состояния.

Каждый performance-кандидат проходит парные Release-измерения на полных
настроенных раундах, с исходным seed/stream scheme, live statistics и совпадением
проверяемых результатов. Нужны отдельные controls: без tracing и с tracing,
обычный sampler и пользовательские измерения, разные workers, короткий запуск
с учётом compilation и длинный steady-state. Локально достаточно затронутых
тестов и новых математических регрессий; широкие release-проверки остаются в CI.

После появления типизированного IR имеет смысл отдельный эксперимент с
ограниченным equality saturation для чистых, доказанно тотальных областей.
E-graph хранит несколько эквивалентных выражений, после чего extractor выбирает
вариант по модели стоимости. Общая идея представлена авторами
[egg](https://egraphs-good.github.io/). Для нашего языка каждой алгебраической
эквивалентности нужны guards: например, `(x / y) * y → x` требует ненулевого
y и сохранения проверок типов и ошибок. Нужны жёсткие лимиты узлов/итераций,
сохранение sharing и оценка compilation cost. Цена узла должна учитывать
измеренные общие операции, а не имя игры. Это возможный следующий уровень
автоматического выбора формы вычисления; он не заменяет анализ эффектов и
не даёт способа угадать отсутствующие авторские правила.

Полные локальные данные исследования: `/tmp/graph-refactor-research/`:
`corpus-inventory.json`, `representation-audit.json`, `loop-workload.json` и
два воспроизводимых скрипта анализа. Полные парные performance reports:
`/tmp/graph-refactor-evidence/paired-v2/`. Эти сгенерированные данные остаются
вне Git согласно политике репозитория.

## Proposed generic primitive: finite-set Compress

The current packed Ancient Dragon line evaluator still repeats a recursive
prefix expression for every authored payline. A 15-second EventPipe capture on
the completed one-worker round attributed 44.10% self CPU to a generated
segment containing line analysis, last-line accounting, scatter/payout writes,
and trace writes. This is a grouped attribution, not a claim that lines alone
consume that share. Previous packed-field work improved full-round throughput
10.0% on Ancient and 11.3% on paid Woodland while the canonical JSON grew.
Repository instructions require a separate proposal and explicit approval
before adding an atom; the operation below is therefore a candidate contract,
not an accepted kernel change. Profiled end-to-end timings remain the
acceptance gate.

For two unsigned finite masks `source` and `selector` in `[0, 2^64 - 1]`, let
`p[0] < p[1] < ... < p[m-1]` be the positions of the set bits in `selector`.
`Compress(source, selector)` returns a mask whose bit `k` equals source bit
`p[k]`, for `0 <= k < m`; every higher result bit is zero. Thus it gathers the
selected source bits in ascending position order into consecutive low bits.
Source bits outside `selector` are ignored. In particular,
`Compress(1<<63, 1<<63) == 1`; the selector's population count does not bound
which source positions may be selected. A zero selector returns zero, and an
all-one selector returns `source`.

The primitive is proposed as an appended `BitSetOp` member, so existing enum
ordinals remain stable. It accepts exactly two existing mask operands, returns
an exact mask, and keeps their eager left-to-right evaluation, conversion,
range checking, and error locations (`bitset.args[0]` / `bitset.args[1]`). No
game, symbol width, grid geometry, or graph-pattern dispatch belongs in the
kernel. The compiled implementation can use BMI2 PEXT when available and a
portable exact fallback elsewhere. Without a useful static selector bound,
shape propagation conservatively reports width 64; a known selector and
source bound can safely refine the result. Exact constants must never pass
through JavaScript floating-point serialization.

This first operation covers the demonstrated gather use. Its inverse,
`Deposit`, is deliberately deferred: a future use case must establish that
generic scatter/placement is needed and benchmark its interaction with current
gravity LUTs and segmented compaction. The game migration itself remains an
editable JSON subgraph composed from generic expressions.

The first authored payoff experiment gathered each entire line and then ran
two SWAR comparisons, wildcard extraction, `First`, and descriptor arithmetic
for every line. Its 774 differential comparisons matched payouts, all original
state, RNG audit, and next RNG word. Yet the same-current-Core ABBA full-round
comparison lost throughput: Ancient 385,245->238,335 rounds/s (0.619 ratio),
Woodland paid 368,973->196,859 (0.534), and Woodland buy 29,496->15,321 (0.519).
All outcome and loop statistics still matched; allocation changed only by a
few bytes. These ranges were disjoint. The grouped profile moved to 59.78%
self in a generated segment that includes line, scatter, payout, and trace
work, so it does not isolate `Compress`. The whole-line SWAR composition was
rejected.

A second, disposable prototype kept the lazy prefix algorithm and used one
`Compress` gather per line. Against the accepted packed lazy-prefix graphs, an
ABBA run on the same rebuilt Core showed overlapping throughput ranges and no
demonstrated gain: Ancient 364,468->361,963 rounds/s (0.993 ratio), Woodland
buy 29,784->28,910 (0.971), and paid Woodland 324,733->314,966 (0.970).
Every measured outcome and loop statistic matched; bytes per round were
unchanged within 0.004 bytes. Canonical JSON shrank only 4.6%, 3.2%, and 4.2%
relative to the accepted packed graphs, which themselves remained about 60%,
34%, and 51% larger than the original graphs. The retained-rate ranges
overlapped in all three cases. This prototype was removed from the worktree;
the accepted packed graphs remain unchanged. The trial does not justify adding
the atom to the kernel for payline evaluation.

Focused `sol_advisor` review caught and corrected one proposed edge rule before
implementation: only `Deposit` ignores source bits above `popcount(selector)`;
`Compress` still sees a selected source bit at position 63 even with a one-bit
selector. The decision to implement Compress alone follows the demonstrated
payline gather; inverse placement is not yet proven necessary. The consultation
changed the boundary test and avoided adding an unused second operation. Token
usage and precise call duration were not exposed by the advisor tool.

The disposable implementation passed its standalone contract test, which
compared every pair of eight-bit source and selector values against an
independent position-enumeration oracle in all sampling tiers. It also checked
software fallback, bit 63, maximum masks, sparse/dense selectors, and malformed
operands/error order. The hybrid game's differential tests compared payout,
every previous state field, RNG audit, and the next RNG word; a selected
49-test regression set passed. Those implementation tests were rolled back
with the prototype and are not part of the current branch. Reconsider this
atom if profiling a different generic operation, such as finite-set compaction,
shows that bit gathering is required and a prototype demonstrates an
end-to-end benefit. The expression-tree UI operation list remains deferred.

## Generic dynamic `DrawMany` for cell refills

The verified Nine Lives TGSlots buy profile showed repeated refill work in the
generic cell-based `tumble`: a 30-cell, single-worker round averaged 1,519.75
RNG words and 2,498.71 small-arena expression elements; the old refill loop
completed 2,222,722 scalar iterations across 16,384 sampled rounds. A 15-second
EventPipe capture attributed stack residence to `SamplingDraw.Pick`, the
refill subgraph, and the fused settle/trace/count group. These are managed
stack samples, not hardware self-CPU. The `PollGC` frame was not treated as GC
cost; measured GC pauses were zero.

The generic `Tumble` authoring now uses the existing dynamic `DrawMany` atom
with count `sizeOf(removed)` and maximum `columns * rows`. It samples through
the same `SamplingDraw.Pick` in the same order, publishes `drops` once, and
restores the old `dropSymbol` value from the last drop. A small no-draw refill
progress loop retains the old loop identity and completion fields: removing
the loop changed `__exitReason_refill` in observable state even though the
board and RNG matched. Its bound remains `cells + 1`; it stops after exactly
the number of batched drops. The extra loop is a compatibility cost and can
be reconsidered only with a generic contract for loop evidence and state.
Cancellation checkpoints are coarser inside the batch: for this 30-cell
board, cancellation arriving after the first check may allow the current
refill batch to consume up to 30 draws before it is observed. The sampler
excludes the interrupted round from payout statistics, but its uncommitted
partial frame is not claimed to match the old per-draw loop.

Dynamic `DrawMany` initially made the small exact Nine Lives models decline
because `Structured.Executor.BatchDraw` required enumerated count support.
The generic exact path now uses candidate lengths `0..MaxCount` only when the
maximum has at most 256 values, and selects them with a partial index. It does
not mark that range as proven support: a count outside the declared bounds
still produces an error. The focused negative test supplies 65 equiprobable counts from 0 through 64
with maximum 30 and confirms that Structured Exact declines instead
of dropping invalid probability mass. Small paid and bought models still
match the same exact rational values. Larger unknown count domains continue
to decline; no approximation or RTP correction is used.

Focused `sol_advisor` review confirmed successful-run RNG equality through the
shared sampler and identified two required semantics: retain `dropSymbol` on a
zero count, and do not infer a total count range from `MaxCount`. The exact
follow-up required a partial selector so invalid values remain errors. This
changed the state-preservation design and exact fallback. Token usage and
precise elapsed time were not exposed by the advisor runtime.

Same-Core Release ABBA measurements on CPU 2, one worker, Tiered defaults,
four processes per graph in ABBA order, three repeats per process with the
first discarded, compared every payout/statistic and loop-evidence row:

| Graph | Scalar refill median | Batched refill median | Ratio | Bytes/round |
| --- | ---: | ---: | ---: | ---: |
| Nine Lives paid | 440,581 (437,868–442,032) | 468,955 (465,936–473,114) | 1.064× | 41.53 → 40.77 |
| Nine Lives buy | 25,513 (25,222–25,577) | 27,888 (26,902–28,139) | 1.093× | 70.74 → 66.45 |

Each retained range is disjoint. A separate differential check replayed 129
seeds including `UInt64.MaxValue`, with tracing both off and on, for each
configuration: **516 comparisons** matched payout, every original state
field, the full RNG audit and the next RNG word. The source exporter verified
all 39 fixtures; scoped ESLint, TypeScript/Vite build, 20 targeted Core tests,
Reformat Code and Full Cleanup passed. The build printed existing dependency
annotation and chunk-size warnings.

Canonical JSON grew from 111,506 to 112,211 bytes for paid and 104,495 to
105,200 for buy; total graph-node counts stayed 131 and 120. This change
improves throughput but does not reduce graph size. The post-change profile
still shows substantial work in the free-spin pass, cascade settle/trace,
cluster pay reads, and `SamplingDrawMany`; these are the next profiler-guided
targets. Evidence is retained outside Git under
`/tmp/modern-primitives-evidence/game-rewrite/nine-lives-drawmany/`.

## Le Militare Super survivor-filter experiment

A fresh clean baseline for `le-militare.super` is 4,660 rounds/s (4,568–4,668)
and 14,552 B/round on one worker pinned to CPU 2, Tiered runtime. The 15-second
EventPipe profile used graph SHA
`ffb5fc4490a6046dc786ad8940ce20312363a5a5faf264b70d8a7eff36fbb912`. Its top
graph run group combines pay-step traces, `emptyBefore`, `columnRemoved`, and
both `survivors` filters; the group had 9.19% self and 21.25% inclusive stack
residence. This is diagnostic grouping, not a per-node instruction
attribution. Per-round arena diagnostics measured 360.47 small-array requests,
7,547.01 requested elements, 35.70 unretained small arrays and 578.88 RNG
words. GC pauses in the throughput baseline were about 2 ms per 1.7 s sample;
`PollGC` profile residence was not used as GC cost.

The isolated prototype filtered an identity position array once and gathered
the old board and carried `held` arrays through those positions. It retained
the original five-row settling LUT and all previous output fields. The
exhaustive Tumble column-pattern test and 46 targeted Le Militare tests passed,
including exact small models, original replays, traces and cross-tier/worker
reproducibility. Same-Core ABBA measurements compared every outcome and loop
statistic: 4,625 rounds/s (4,603–4,718) for the original graph versus 4,671
(4,521–4,952) for the prototype, a 1.010 ratio with overlapping ranges.
Allocation increased from 14,553 to 14,778 B/round. Canonical JSON grew from
286,185 to 287,313 bytes and the source plus mechanic node count rose from 230
to 232. The experiment was removed: it did not establish a performance win
and made the graph larger.

The focused Sol review recommended this as the smaller experiment than
replacing the LUT with `SegmentedCompact`, but only with fixed aligned board
and carried-array lengths. It also warned that malformed mismatched arrays
could observe a different error order. The experiment confirmed the valid
game cases but did not add a general malformed-array compatibility test. The
advice narrowed the prototype; the measured result rejected it. Token usage
and exact advisor elapsed time were not exposed.

### Ordered component state-machine experiment and record execution

The Le Militare experiment replaced the nested symbol/component graph loops with a bounded Fold state machine. A transition either selected the first unpaid symbol or processed its next reachable component. A private pending flag retained symbol order and wild claims. There were at most `cells + payingSymbols` transitions: every group consumed a distinct seed from its symbol's cells. Twelve-transition chunks stayed below the existing 10,000-operation expression budget; the outer chunk loop had one iteration of slack.

The candidate passed 528 successful round comparisons across eight Le configurations, trace off/on, seeds 0–31 and UInt64.MaxValue. Payout, every original gameplay/scratch field, RNG audit and continuation matched. Counters and evidence of the replaced cluster loops intentionally differed; this was not complete old-state or cancellation-state equivalence.

It was rejected as the production composition. Matched diagnostic runs used one worker pinned to CPU 2, the same Release Core, Compiled tier, 4,096 warm-up rounds and two 4,096-round measurements. The existing graph reached 4,737–4,776 full rounds/s; the final candidate reached 2,555–2,719. Its canonical JSON also grew. Le's production graph was restored. These are diagnostic observations, not a claim of a speedup or achievement of the million-round target.

Allocation profiling exposed a generic execution gap: materializing record accumulators constructed dictionary entries, boxed ExprValue values, arrays and copied dictionaries. Record-fold scalar replacement keeps normalized read fields separate from raw next-result fields, preserving simultaneous recurrence and final Symbol kinds. It accepts matching static Record/Tuple schemas and Let chains, preserves interpreted-child publication/restoration, and otherwise keeps the existing path. Owned record rentals now share immutable ordinal layouts and reuse bounded per-round payloads. Typed record projection avoids boxing. Measurements, collectors and persistent state disable the arena; state export recursively detaches values. Public ExprValue.Record continues to copy caller-owned collections. Malformed record names retain the original eager evaluation and validation path.

The candidate's allocations fell from approximately 517 KB to 15.6 KB per round, but that did not make its CPU execution competitive. The generic mechanisms are independently regression-tested; the graph rewrite is not adopted merely because its allocations improved. Further work should measure native control-flow regions and mask publication rather than repeat the same record-based game migration.

Two focused Sol reviews in this stage examined scalar replacement and owned-record lifetime. They led to fallback/restoration, recursive normalization, alias, shape-cache, exceptional construction, export and pool-budget tests. They did not resolve the CPU performance blocker. Advisor token use and exact consultation wall time were not exposed/recorded. Raw diagnostics and the rejected candidate remain in the local `le-fold-components` evidence directory, outside Git.
