using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LaPlateformeDeLemploiAPI.Migrations
{
    /// <inheritdoc />
    public partial class SeedData100Offers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "Description", "LogoUrl" },
                values: new object[] { new DateTime(2022, 1, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Leader français du développement logiciel, spécialisé dans les solutions SaaS et cloud pour les entreprises du CAC 40. Plus de 500 collaborateurs répartis sur 3 sites en France.", "https://ui-avatars.com/api/?name=TC&background=065f46&color=fff&size=128" });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "Description" },
                values: new object[] { new DateTime(2023, 2, 20, 0, 0, 0, 0, DateTimeKind.Utc), "Startup spécialisée en intelligence artificielle et analyse de données massives. Lauréate du prix French Tech 2025, nous développons des solutions de NLP et computer vision pour la santé et la finance." });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "Description" },
                values: new object[] { new DateTime(2021, 3, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Expert en solutions d'infrastructure cloud et DevOps. Partenaire certifié AWS, Azure et GCP, nous accompagnons les ETI et grands comptes dans leur migration et l'optimisation de leurs architectures." });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "Description", "LogoUrl" },
                values: new object[] { new DateTime(2022, 4, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Agence de design UX/UI primée, créant des expériences numériques mémorables pour des clients internationaux. Spécialistes du design system, de l'accessibilité et du design thinking.", "https://ui-avatars.com/api/?name=DH&background=ea580c&color=fff&size=128" });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreatedAt", "Description" },
                values: new object[] { new DateTime(2020, 5, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cabinet de conseil en cybersécurité de référence, protégeant les entreprises du CAC 40 et les institutions publiques. Certifications ANSSI, ISO 27001 et expertise en réponse à incidents." });

            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "CreatedAt", "Description", "Location", "LogoUrl", "Name", "Website" },
                values: new object[,]
                {
                    { 6, new DateTime(2023, 6, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Éditeur de logiciels dédiés à la transition écologique. Nos outils de mesure d'empreinte carbone et de reporting ESG sont utilisés par plus de 2 000 entreprises en Europe.", "Montpellier", "https://ui-avatars.com/api/?name=GT&background=16a34a&color=fff&size=128", "GreenTech Solutions", "https://greentech-sol.fr" },
                    { 7, new DateTime(2022, 7, 8, 0, 0, 0, 0, DateTimeKind.Utc), "Fintech en forte croissance développant une néo-banque pour les freelances et TPE. Série B de 45M€ levée en 2025, équipe de 120 personnes avec une culture produit forte.", "Paris", "https://ui-avatars.com/api/?name=FS&background=0284c7&color=fff&size=128", "FinStart", "https://finstart.io" },
                    { 8, new DateTime(2019, 8, 22, 0, 0, 0, 0, DateTimeKind.Utc), "Groupe média digital leader en France avec un portefeuille de 15 sites éditoriaux cumulant 80 millions de visiteurs uniques par mois. Innovation constante en monétisation et expérience lecteur.", "Paris", "https://ui-avatars.com/api/?name=MS&background=dc2626&color=fff&size=128", "MédiaSphère", "https://mediasphere.fr" },
                    { 9, new DateTime(2021, 9, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Centre de R&D en robotique et systèmes embarqués, collaborant avec l'industrie automobile et aérospatiale. Nos algorithmes de perception et de planification équipent des robots déployés dans 12 pays.", "Grenoble", "https://ui-avatars.com/api/?name=RL&background=9333ea&color=fff&size=128", "Robotix Lab", "https://robotixlab.com" },
                    { 10, new DateTime(2022, 10, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Plateforme de e-santé connectant patients, médecins et pharmaciens. Application mobile utilisée par 3 millions d'utilisateurs, téléconsultation, ordonnance numérique et suivi médical.", "Strasbourg", "https://ui-avatars.com/api/?name=SC&background=0d9488&color=fff&size=128", "SantéConnect", "https://santeconnect.fr" },
                    { 11, new DateTime(2020, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Éditeur de solutions logistiques et supply chain. Notre TMS et WMS nouvelle génération optimisent les flux de plus de 800 entrepôts en Europe grâce à l'IA prédictive.", "Lille", "https://ui-avatars.com/api/?name=LF&background=ca8a04&color=fff&size=128", "LogiFlow", "https://logiflow.eu" },
                    { 12, new DateTime(2023, 1, 25, 0, 0, 0, 0, DateTimeKind.Utc), "EdTech française révolutionnant la formation professionnelle avec des parcours adaptatifs basés sur l'IA. Partenaire de 200 organismes de formation et 50 grandes écoles.", "Rennes", "https://ui-avatars.com/api/?name=EN&background=2563eb&color=fff&size=128", "EduNova", "https://edunova.fr" },
                    { 13, new DateTime(2022, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "PropTech qui digitalise la gestion immobilière. Notre plateforme tout-en-un couvre la gestion locative, la comptabilité, les états des lieux numériques et la relation copropriétaire.", "Marseille", "https://ui-avatars.com/api/?name=IP&background=b45309&color=fff&size=128", "ImmoPilot", "https://immopilot.fr" },
                    { 14, new DateTime(2021, 5, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Plateforme de réservation de voyages sur mesure utilisant l'IA pour personnaliser les itinéraires. 500 000 voyageurs accompagnés en 2025, présence dans 8 pays européens.", "Nice", "https://ui-avatars.com/api/?name=VG&background=e11d48&color=fff&size=128", "Voyageo", "https://voyageo.com" },
                    { 15, new DateTime(2019, 7, 11, 0, 0, 0, 0, DateTimeKind.Utc), "Acteur majeur de la sécurité bancaire et de la conformité réglementaire. Solutions anti-fraude, KYC automatisé et monitoring transactionnel pour les établissements financiers européens.", "Paris", "https://ui-avatars.com/api/?name=SB&background=1e3a5f&color=fff&size=128", "SecureBank", "https://securebank.eu" },
                    { 16, new DateTime(2022, 9, 7, 0, 0, 0, 0, DateTimeKind.Utc), "AgriTech développant des solutions IoT et data analytics pour l'agriculture de précision. Capteurs connectés, imagerie satellite et algorithmes d'optimisation pour 5 000 exploitations.", "Toulouse", "https://ui-avatars.com/api/?name=AS&background=15803d&color=fff&size=128", "AgriSmart", "https://agrismart.fr" },
                    { 17, new DateTime(2020, 12, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Studio de jeux vidéo indépendant créant des expériences narratives primées. Équipe de 60 passionnés, moteur propriétaire et communauté de 2 millions de joueurs actifs.", "Lyon", "https://ui-avatars.com/api/?name=PF&background=be123c&color=fff&size=128", "PlayForge", "https://playforge.games" },
                    { 18, new DateTime(2023, 4, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Plateforme d'automatisation juridique utilisant le NLP pour analyser contrats, clauses et jurisprudence. Utilisée par 300 cabinets d'avocats et directions juridiques en France.", "Bordeaux", "https://ui-avatars.com/api/?name=LT&background=4338ca&color=fff&size=128", "LegalTech Plus", "https://legaltechplus.fr" },
                    { 19, new DateTime(2021, 6, 20, 0, 0, 0, 0, DateTimeKind.Utc), "Laboratoire d'innovation urbaine travaillant avec les collectivités sur la mobilité intelligente, la gestion énergétique des bâtiments et les jumeaux numériques urbains.", "Nantes", "https://ui-avatars.com/api/?name=SL&background=0e7490&color=fff&size=128", "SmartCity Lab", "https://smartcitylab.fr" },
                    { 20, new DateTime(2023, 8, 9, 0, 0, 0, 0, DateTimeKind.Utc), "Startup foodtech optimisant la chaîne d'approvisionnement alimentaire grâce à la blockchain et l'IA. Traçabilité complète du producteur au consommateur pour 150 enseignes de distribution.", "Montpellier", "https://ui-avatars.com/api/?name=FC&background=c2410c&color=fff&size=128", "FoodChain", "https://foodchain.io" }
                });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "PublishedAt" },
                values: new object[] { "Rejoignez notre équipe produit pour développer notre plateforme SaaS utilisée par 10 000 entreprises. Vous concevrez des fonctionnalités end-to-end avec Angular 19 côté front et .NET 8 / SQL Server côté back, le tout déployé sur Azure. Méthodologie Scrum, code reviews systématiques et CI/CD automatisé.", new DateTime(2026, 1, 6, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CategoryId", "CompanyId", "Description", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 1, 1, "Nous recherchons un développeur React confirmé pour créer des interfaces performantes et accessibles. Vous travaillerez sur notre design system, optimiserez le rendering et implémenterez des animations fluides. Stack : React 19, TypeScript, Zustand, Vitest, Storybook.", "Paris", new DateTime(2026, 1, 10, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Développeur Frontend React" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CategoryId", "CompanyId", "Description", "IsRemote", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 1, 1, "En tant que Tech Lead, vous encadrerez une équipe de 5 développeurs backend. Vous définirez l'architecture microservices, garantirez la qualité du code et serez garant des performances. Stack : Java 21, Spring Boot 3, PostgreSQL, Kafka, Kubernetes.", true, "Paris", new DateTime(2026, 1, 15, 0, 0, 0, 0, DateTimeKind.Utc), 78000m, 60000m, "Tech Lead Backend Java / Spring" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CategoryId", "CompanyId", "ContractType", "Description", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 7, 1, "CDI", "Pilotez des projets de transformation digitale pour nos clients grands comptes. Méthodologie Agile (Scrum/SAFe), gestion budgétaire, coordination d'équipes pluridisciplinaires de 8 à 15 personnes. Certifications PMP ou PRINCE2 appréciées.", "Paris", new DateTime(2026, 1, 20, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 50000m, "Chef de Projet Digital" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CategoryId", "CompanyId", "Description", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 1, 1, "Développez et maintenez les frameworks de tests automatisés pour garantir la qualité de nos releases. Vous rédigerez des plans de test, automatiserez les scénarios critiques et participerez à l'amélioration continue du pipeline CI. Playwright, Cypress, k6.", "Paris", new DateTime(2026, 1, 25, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Ingénieur QA Automatisation" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "ContractType", "Description", "IsRemote", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { "Stage", "Stage de 6 mois au sein de l'équipe mobile. Vous participerez au développement de notre application cross-platform en Flutter/Dart, intégrerez des APIs REST et contribuerez aux tests unitaires. Encadrement par un lead mobile expérimenté.", false, new DateTime(2026, 2, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1400m, 1000m, "Stage Développeur Mobile Flutter" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "ContractType", "Description", "IsRemote", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { "CDI", "Concevez et déployez des modèles de machine learning en production pour nos clients santé et finance. Travail sur des problématiques de NLP, computer vision et séries temporelles. Stack : Python, PyTorch, MLflow, Spark, AWS SageMaker.", true, new DateTime(2026, 1, 8, 0, 0, 0, 0, DateTimeKind.Utc), 75000m, 55000m, "Data Scientist Senior" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CategoryId", "CompanyId", "Description", "Location", "PublishedAt", "SalaryMax", "Title" },
                values: new object[] { 3, 2, "Industrialisez les modèles développés par l'équipe Data Science. Vous construirez les pipelines d'entraînement, de serving et de monitoring. Maîtrise de Kubernetes, Docker, FastAPI et des principes MLOps indispensable.", "Lyon", new DateTime(2026, 1, 14, 0, 0, 0, 0, DateTimeKind.Utc), 68000m, "ML Engineer" });

            migrationBuilder.InsertData(
                table: "JobOffers",
                columns: new[] { "Id", "CategoryId", "CompanyId", "ContractType", "Description", "ExpiresAt", "IsActive", "IsRemote", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[,]
                {
                    { 9, 3, 2, "CDI", "Analysez les données métier pour fournir des insights actionnables aux équipes produit et marketing. Création de dashboards interactifs, modélisation statistique et automatisation de rapports. SQL, Python, dbt, Looker.", null, true, false, "Lyon", new DateTime(2026, 1, 22, 0, 0, 0, 0, DateTimeKind.Utc), 48000m, 38000m, "Data Analyst" },
                    { 10, 3, 2, "Stage", "Stage de 6 mois au sein de l'équipe Data. Vous participerez à l'analyse exploratoire, la création de dashboards Power BI et l'automatisation de rapports avec Python et SQL. Accompagnement personnalisé et montée en compétences garantie.", null, true, false, "Lyon", new DateTime(2026, 2, 5, 0, 0, 0, 0, DateTimeKind.Utc), 1200m, 900m, "Stage Data Analyst" },
                    { 11, 3, 2, "CDI", "Construisez et optimisez notre data lakehouse pour traiter des volumes de plusieurs To par jour. Conception de pipelines ETL/ELT robustes et scalables. Stack : Spark, Airflow, dbt, Snowflake, Terraform.", null, true, true, "Lyon", new DateTime(2026, 2, 12, 0, 0, 0, 0, DateTimeKind.Utc), 62000m, 48000m, "Data Engineer" },
                    { 12, 4, 3, "CDI", "Assurez la fiabilité et la scalabilité de notre infrastructure multi-cloud. Vous automatiserez les déploiements, mettrez en place le monitoring et optimiserez les coûts. Kubernetes, Terraform, ArgoCD, Prometheus, Grafana.", null, true, false, "Bordeaux", new DateTime(2026, 1, 12, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 50000m, "Ingénieur DevOps / SRE" },
                    { 13, 4, 3, "CDI", "Concevez des architectures cloud résilientes et sécurisées pour nos clients. Certification AWS Solutions Architect Professional requise. Vous réaliserez des audits, définirez les bonnes pratiques et accompagnerez les migrations.", null, true, true, "Bordeaux", new DateTime(2026, 1, 18, 0, 0, 0, 0, DateTimeKind.Utc), 82000m, 62000m, "Architecte Cloud AWS" },
                    { 14, 4, 3, "CDI", "Administrez et faites évoluer nos clusters Kubernetes de production (300+ pods). Mise en place de service mesh, gestion des secrets, optimisation des ressources et formation des équipes de développement.", null, true, true, "Bordeaux", new DateTime(2026, 2, 3, 0, 0, 0, 0, DateTimeKind.Utc), 67000m, 52000m, "Ingénieur Plateforme Kubernetes" },
                    { 15, 4, 3, "Alternance", "Alternance de 12 mois pour préparer votre diplôme d'ingénieur tout en travaillant sur des projets cloud réels. Vous participerez au provisionning d'infrastructures, aux migrations et à l'automatisation.", null, true, false, "Bordeaux", new DateTime(2026, 2, 18, 0, 0, 0, 0, DateTimeKind.Utc), 1500m, 1100m, "Alternance Ingénieur Cloud" },
                    { 16, 8, 3, "CDI", "Gérez notre parc de serveurs Linux (Debian/Ubuntu) hébergeant les environnements de staging et production. Scripting Bash/Python, gestion des sauvegardes, hardening sécurité et astreintes mensuelles.", null, true, false, "Bordeaux", new DateTime(2026, 2, 25, 0, 0, 0, 0, DateTimeKind.Utc), 48000m, 38000m, "Administrateur Systèmes Linux" },
                    { 17, 2, 4, "CDI", "Concevez des interfaces utilisateur exceptionnelles pour nos clients internationaux. Vous mènerez des recherches utilisateurs, créerez des wireframes et prototypes haute fidélité, et maintiendrez notre design system. Figma, Principle, Maze.", null, true, true, "Nantes", new DateTime(2026, 1, 9, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "UX/UI Designer Senior" },
                    { 18, 2, 4, "CDI", "En tant que Product Designer, vous accompagnerez une squad produit de la discovery au delivery. Tests utilisateurs, A/B testing, design sprint et collaboration étroite avec les PO et développeurs. Expérience en SaaS B2B requise.", null, true, true, "Nantes", new DateTime(2026, 1, 16, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Product Designer" },
                    { 19, 2, 4, "CDD", "Créez des animations et micro-interactions pour enrichir l'expérience utilisateur de nos produits digitaux. After Effects, Lottie, CSS animations. Collaboration avec l'équipe UX pour définir les guidelines d'animation.", null, true, false, "Nantes", new DateTime(2026, 1, 28, 0, 0, 0, 0, DateTimeKind.Utc), 42000m, 35000m, "Motion Designer" },
                    { 20, 2, 4, "Stage", "Stage de 6 mois en recherche utilisateur. Vous organiserez et conduirez des entretiens, tests d'utilisabilité et analyses quantitatives pour améliorer nos produits. Formation au design thinking et aux méthodes lean UX.", null, true, false, "Nantes", new DateTime(2026, 2, 8, 0, 0, 0, 0, DateTimeKind.Utc), 1100m, 900m, "Stage UX Research" },
                    { 21, 2, 4, "CDI", "Définissez l'identité visuelle des projets digitaux de l'agence. Direction créative, supervision de l'équipe design (4 personnes), présentation client et veille tendances. Portfolio démontrant une vision forte exigé.", null, true, false, "Nantes", new DateTime(2026, 2, 20, 0, 0, 0, 0, DateTimeKind.Utc), 62000m, 48000m, "Directeur Artistique Digital" },
                    { 22, 5, 5, "CDI", "Réalisez des tests d'intrusion (web, mobile, infra), des audits de code et accompagnez nos clients dans leur stratégie de sécurité. Certifications OSCP ou CEH appréciées. Déplacements ponctuels chez les clients.", null, true, false, "Toulouse", new DateTime(2026, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), 68000m, 48000m, "Consultant Cybersécurité" },
                    { 23, 5, 5, "CDI", "Surveillez et analysez les événements de sécurité dans notre SOC 24/7. Investigation d'incidents, threat hunting, rédaction de règles de détection SIEM (Splunk/ELK) et amélioration continue des processus de réponse.", null, true, false, "Toulouse", new DateTime(2026, 1, 19, 0, 0, 0, 0, DateTimeKind.Utc), 58000m, 42000m, "Analyste SOC N2/N3" },
                    { 24, 5, 5, "CDI", "Sécurisez les environnements cloud de nos clients (AWS, Azure, GCP). Audit de configurations, mise en place de CSPM, gestion des identités (IAM) et conformité réglementaire (NIS2, DORA).", null, true, true, "Toulouse", new DateTime(2026, 2, 2, 0, 0, 0, 0, DateTimeKind.Utc), 70000m, 52000m, "Ingénieur Sécurité Cloud" },
                    { 25, 5, 5, "CDI", "Assistez le RSSI dans la définition et la mise en œuvre de la politique de sécurité. Pilotage des audits, gestion des risques, sensibilisation des collaborateurs et veille réglementaire. 5 ans d'expérience minimum en cybersécurité.", null, true, false, "Toulouse", new DateTime(2026, 2, 15, 0, 0, 0, 0, DateTimeKind.Utc), 85000m, 65000m, "RSSI Adjoint" },
                    { 26, 5, 5, "Alternance", "Alternance de 24 mois pour développer vos compétences en cybersécurité opérationnelle. Participation aux audits, veille vulnérabilités, rédaction de rapports et contribution aux outils internes de détection.", null, true, false, "Toulouse", new DateTime(2026, 3, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1400m, 1000m, "Alternance Analyste Cybersécurité" },
                    { 27, 1, 6, "CDI", "Développez les APIs de notre plateforme de mesure d'empreinte carbone utilisée par 2 000 entreprises. Architecture propre, tests exhaustifs, documentation OpenAPI. Python 3.12, Django REST Framework, Celery, Redis.", null, true, true, "Montpellier", new DateTime(2026, 1, 11, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Développeur Backend Python / Django" },
                    { 28, 1, 6, "CDI", "Rejoignez l'équipe frontend pour construire les interfaces de reporting ESG. Composants réutilisables, visualisations de données complexes avec D3.js et accessibilité WCAG 2.1 AA. Vue 3, TypeScript, Pinia, Vitest.", null, true, true, "Montpellier", new DateTime(2026, 1, 24, 0, 0, 0, 0, DateTimeKind.Utc), 50000m, 38000m, "Développeur Frontend Vue.js" },
                    { 29, 7, 6, "CDI", "Coordonnez les projets liant stratégie RSE et outils digitaux pour nos clients. Compréhension des enjeux climatiques, gestion de projet Agile, animation d'ateliers et reporting aux parties prenantes.", null, true, false, "Montpellier", new DateTime(2026, 2, 7, 0, 0, 0, 0, DateTimeKind.Utc), 56000m, 44000m, "Chef de Projet RSE & Digital" },
                    { 30, 6, 6, "CDD", "Créez du contenu éditorial (articles, livres blancs, newsletters) autour de la transition écologique et du reporting ESG. SEO, stratégie éditoriale et collaboration avec les experts métier.", null, true, true, "Montpellier", new DateTime(2026, 2, 22, 0, 0, 0, 0, DateTimeKind.Utc), 40000m, 32000m, "Content Manager Développement Durable" },
                    { 31, 1, 6, "Stage", "Stage de 6 mois pour participer au développement de nouvelles fonctionnalités de notre plateforme. Vous toucherez au front (Vue.js), au back (Python/Django) et aux bases de données. Encadrement technique quotidien.", null, true, false, "Montpellier", new DateTime(2026, 3, 5, 0, 0, 0, 0, DateTimeKind.Utc), 1200m, 900m, "Stage Développeur Full Stack" },
                    { 32, 1, 7, "CDI", "Développez les microservices de notre néo-banque en Go. Traitement de transactions en temps réel, haute disponibilité et conformité réglementaire bancaire. Go, gRPC, PostgreSQL, Kafka, Kubernetes.", null, true, true, "Paris", new DateTime(2026, 1, 13, 0, 0, 0, 0, DateTimeKind.Utc), 72000m, 52000m, "Développeur Backend Go" },
                    { 33, 7, 7, "CDI", "Définissez la roadmap de notre module de paiement (virement, prélèvement, carte). Analyse concurrentielle, spécifications fonctionnelles, suivi des métriques et collaboration avec les régulateurs (ACPR).", null, true, true, "Paris", new DateTime(2026, 1, 21, 0, 0, 0, 0, DateTimeKind.Utc), 70000m, 55000m, "Product Manager Paiements" },
                    { 34, 1, 7, "CDI", "Développez de nouvelles fonctionnalités pour notre application bancaire iOS (500K+ téléchargements). Swift, SwiftUI, Combine, architecture MVVM-C, intégration biométrique et paiement NFC.", null, true, true, "Paris", new DateTime(2026, 2, 4, 0, 0, 0, 0, DateTimeKind.Utc), 63000m, 48000m, "Développeur Mobile iOS Swift" },
                    { 35, 6, 7, "CDI", "Pilotez la stratégie d'acquisition et de rétention de notre néo-banque. SEA, ASO, influence, referral programs et analyse de cohortes. Objectif : doubler la base utilisateurs en 12 mois.", null, true, false, "Paris", new DateTime(2026, 2, 17, 0, 0, 0, 0, DateTimeKind.Utc), 60000m, 45000m, "Growth Marketing Manager" },
                    { 36, 7, 7, "CDD", "Assistez l'équipe conformité dans le suivi réglementaire (DSP2, LCB-FT, RGPD). Contrôle des opérations, rédaction de procédures internes et veille réglementaire. Formation juridique ou finance appréciée.", null, true, false, "Paris", new DateTime(2026, 3, 2, 0, 0, 0, 0, DateTimeKind.Utc), 42000m, 35000m, "Compliance Officer Junior" },
                    { 37, 1, 8, "CDI", "Maintenez et faites évoluer nos CMS maison propulsant 15 sites éditoriaux à fort trafic. Optimisation des performances, cache avancé, intégration de flux publicitaires. PHP 8.3, Symfony 7, Elasticsearch, Varnish.", null, true, true, "Paris", new DateTime(2026, 1, 17, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Développeur PHP / Symfony" },
                    { 38, 6, 8, "CDI", "Définissez et exécutez la stratégie SEO de notre portefeuille de sites (80M de VU/mois). Audit technique, stratégie de contenu, netlinking, suivi des Core Web Vitals et coordination avec les équipes tech et éditoriales.", null, true, true, "Paris", new DateTime(2026, 1, 29, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "SEO Manager" },
                    { 39, 6, 8, "CDI", "Optimisez nos revenus publicitaires programmatiques (header bidding, deals privés, native ads). Analyse des performances, A/B testing de formats et relation avec les SSP et trading desks.", null, true, false, "Paris", new DateTime(2026, 2, 10, 0, 0, 0, 0, DateTimeKind.Utc), 48000m, 38000m, "Traffic Manager Programmatique" },
                    { 40, 6, 8, "CDD", "Couvrez l'actualité tech et startup pour notre site dédié à l'innovation. Rédaction d'articles, interviews, live-tweets d'événements et production de podcasts. Passion pour la tech et excellent style rédactionnel exigés.", null, true, true, "Paris", new DateTime(2026, 2, 24, 0, 0, 0, 0, DateTimeKind.Utc), 38000m, 30000m, "Journaliste Web Tech" },
                    { 41, 6, 8, "Alternance", "Alternance de 12 mois pour animer nos communautés sur les réseaux sociaux (2M+ d'abonnés cumulés). Création de contenus, modération, analyse des KPIs et veille tendances. Créativité et réactivité indispensables.", null, true, false, "Paris", new DateTime(2026, 3, 8, 0, 0, 0, 0, DateTimeKind.Utc), 1200m, 900m, "Alternance Community Manager" },
                    { 42, 3, 9, "CDI", "Développez les algorithmes de perception et de planification de nos robots autonomes. C++17, ROS2, OpenCV, PCL. Travail en collaboration avec les équipes hardware et les partenaires industriels.", null, true, false, "Grenoble", new DateTime(2026, 1, 16, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 48000m, "Ingénieur Robotique C++ / ROS" },
                    { 43, 3, 9, "CDI", "Concevez des systèmes de vision par ordinateur pour la détection d'objets et la navigation autonome. Deep learning (YOLO, Transformers), calibration caméra, optimisation embarquée (TensorRT, ONNX).", null, true, false, "Grenoble", new DateTime(2026, 1, 30, 0, 0, 0, 0, DateTimeKind.Utc), 68000m, 50000m, "Ingénieur Computer Vision" },
                    { 44, 1, 9, "CDI", "Développez le firmware de nos cartes de contrôle moteur et de nos capteurs. C/C++ embarqué, RTOS (FreeRTOS/Zephyr), protocoles CAN/SPI/I2C, debug hardware. Expérience dans l'industrie ou la robotique appréciée.", null, true, false, "Grenoble", new DateTime(2026, 2, 13, 0, 0, 0, 0, DateTimeKind.Utc), 60000m, 45000m, "Ingénieur Systèmes Embarqués" },
                    { 45, 3, 9, "CDD", "Thèse de 3 ans en partenariat avec le CNRS sur les algorithmes de SLAM et de planification de trajectoire en environnement dynamique. Publication dans des conférences internationales (ICRA, IROS).", null, true, false, "Grenoble", new DateTime(2026, 3, 3, 0, 0, 0, 0, DateTimeKind.Utc), 28000m, 24000m, "Thèse CIFRE — Navigation Autonome" },
                    { 46, 1, 10, "CDI", "Développez notre application de e-santé utilisée par 3 millions de patients. Fonctionnalités de téléconsultation, ordonnance numérique et suivi médical. Kotlin, Jetpack Compose, Hilt, Room, WebRTC.", null, true, true, "Strasbourg", new DateTime(2026, 1, 20, 0, 0, 0, 0, DateTimeKind.Utc), 57000m, 43000m, "Développeur Mobile Android Kotlin" },
                    { 47, 1, 10, "CDI", "Construisez les APIs de notre plateforme de santé connectée. Conformité HDS (Hébergeur de Données de Santé), chiffrement bout en bout et interopérabilité HL7/FHIR. Node.js, NestJS, MongoDB, RabbitMQ.", null, true, true, "Strasbourg", new DateTime(2026, 2, 1, 0, 0, 0, 0, DateTimeKind.Utc), 56000m, 42000m, "Développeur Backend Node.js" },
                    { 48, 7, 10, "CDI", "Définissez la vision produit de notre parcours patient digital. Interviews utilisateurs (patients, médecins, pharmaciens), priorisation des fonctionnalités et suivi des indicateurs santé. Connaissance du secteur e-santé requise.", null, true, false, "Strasbourg", new DateTime(2026, 2, 16, 0, 0, 0, 0, DateTimeKind.Utc), 60000m, 48000m, "Chef de Produit Santé Numérique" },
                    { 49, 8, 10, "CDI", "Assurez le support technique de notre plateforme auprès des professionnels de santé. Diagnostic des incidents, escalade, rédaction de procédures et formation des utilisateurs. Astreintes week-end en rotation.", null, true, false, "Strasbourg", new DateTime(2026, 3, 1, 0, 0, 0, 0, DateTimeKind.Utc), 35000m, 28000m, "Technicien Support Niveau 2" },
                    { 50, 8, 10, "Stage", "Stage de 6 mois pour participer à l'assurance qualité de notre application mobile et web. Rédaction de cas de test, tests manuels et automatisés, reporting de bugs et suivi des corrections.", null, true, false, "Strasbourg", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), 1100m, 800m, "Stage QA / Testeur" },
                    { 51, 1, 11, "CDI", "Développez notre TMS nouvelle génération. Fonctionnalités de tracking en temps réel, optimisation d'itinéraires et intégration avec les ERP clients. .NET 8, Angular 18, SignalR, SQL Server, Azure Maps.", null, true, true, "Lille", new DateTime(2026, 1, 23, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Développeur Full Stack .NET / Angular" },
                    { 52, 3, 11, "CDI", "Développez des modèles de prévision de la demande et d'optimisation logistique. Algorithmes d'optimisation combinatoire, time-series forecasting et simulation Monte Carlo. Python, OR-Tools, scikit-learn.", null, true, true, "Lille", new DateTime(2026, 2, 6, 0, 0, 0, 0, DateTimeKind.Utc), 62000m, 48000m, "Data Scientist Supply Chain" },
                    { 53, 7, 11, "CDI", "Facilitez le travail de 2 squads produit (12 personnes). Animation des cérémonies Scrum, levée des obstacles, amélioration continue des pratiques et coaching des équipes. Certification PSM II ou équivalent requise.", null, true, false, "Lille", new DateTime(2026, 2, 19, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 45000m, "Scrum Master" },
                    { 54, 8, 11, "CDI", "Administrez nos bases SQL Server de production (5 To+). Optimisation des requêtes, plans de maintenance, haute disponibilité (Always On), monitoring des performances et gestion des sauvegardes/restaurations.", null, true, false, "Lille", new DateTime(2026, 3, 4, 0, 0, 0, 0, DateTimeKind.Utc), 50000m, 40000m, "Administrateur Base de Données SQL Server" },
                    { 55, 1, 11, "Alternance", "Alternance de 12 mois au sein de l'équipe R&D. Vous développerez des interfaces web, intégrerez des APIs et participerez aux revues de code. Stack .NET / Angular, méthodologie Scrum.", null, true, false, "Lille", new DateTime(2026, 3, 15, 0, 0, 0, 0, DateTimeKind.Utc), 1300m, 1000m, "Alternance Développeur Web" },
                    { 56, 1, 12, "CDI", "Développez les interfaces de notre plateforme LMS utilisée par 200 organismes de formation. Parcours adaptatifs, éditeur de contenu WYSIWYG et tableaux de bord apprenants. Angular 19, RxJS, NgRx, Material.", null, true, true, "Rennes", new DateTime(2026, 1, 26, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Développeur Frontend Angular" },
                    { 57, 7, 12, "CDI", "Concevez des parcours de formation innovants et engageants. Scénarisation pédagogique, gamification, évaluation par compétences et intégration d'outils IA d'apprentissage adaptatif. Expérience en ingénierie de formation requise.", null, true, true, "Rennes", new DateTime(2026, 2, 9, 0, 0, 0, 0, DateTimeKind.Utc), 45000m, 35000m, "Ingénieur Pédagogique Digital" },
                    { 58, 3, 12, "CDD", "Analysez les données d'apprentissage pour améliorer les parcours de formation. Taux de complétion, prédiction de décrochage, segmentation des apprenants. SQL, Python, Metabase, Google Analytics 4.", null, true, false, "Rennes", new DateTime(2026, 2, 23, 0, 0, 0, 0, DateTimeKind.Utc), 42000m, 34000m, "Data Analyst EdTech" },
                    { 59, 8, 12, "CDI", "Accompagnez nos clients (organismes de formation et grandes écoles) dans l'adoption de notre plateforme. Onboarding, formation, suivi de la satisfaction et identification d'opportunités d'upsell.", null, true, false, "Rennes", new DateTime(2026, 3, 7, 0, 0, 0, 0, DateTimeKind.Utc), 42000m, 33000m, "Customer Success Manager" },
                    { 60, 6, 12, "Stage", "Stage de 6 mois pour contribuer à la stratégie d'acquisition digitale. Création de contenus, gestion des campagnes emailing, webinaires et analyse des performances. Passion pour l'éducation et le digital.", null, true, false, "Rennes", new DateTime(2026, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), 1000m, 800m, "Stage Marketing Digital EdTech" },
                    { 61, 1, 13, "CDI", "Développez notre plateforme de gestion immobilière. Modules de gestion locative, comptabilité, états des lieux numériques avec signature électronique. Ruby 3.3, Rails 7, PostgreSQL, Sidekiq, Hotwire.", null, true, true, "Marseille", new DateTime(2026, 1, 27, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Développeur Full Stack Ruby on Rails" },
                    { 62, 7, 13, "CDI", "Définissez la roadmap de notre module de gestion locative. Interviews clients (agences immobilières, bailleurs sociaux), spécifications fonctionnelles, priorisation et suivi des métriques produit.", null, true, false, "Marseille", new DateTime(2026, 2, 11, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Product Owner Gestion Locative" },
                    { 63, 2, 13, "CDD", "Améliorez l'expérience utilisateur de notre application pour les gestionnaires immobiliers. User research, personas, parcours utilisateurs, prototypage et tests. Connaissance du secteur immobilier un plus.", null, true, true, "Marseille", new DateTime(2026, 2, 26, 0, 0, 0, 0, DateTimeKind.Utc), 44000m, 36000m, "UX Designer Immobilier" },
                    { 64, 1, 13, "CDI", "Développez notre application mobile de gestion immobilière (iOS/Android). États des lieux avec photos, signature électronique, notifications push. React Native, TypeScript, Expo, Redux Toolkit.", null, true, true, "Marseille", new DateTime(2026, 3, 10, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Développeur Mobile React Native" },
                    { 65, 8, 13, "CDI", "Assurez le support technique et fonctionnel de notre plateforme auprès des professionnels de l'immobilier. Diagnostic, résolution d'incidents, formation utilisateurs et remontée de bugs à l'équipe technique.", null, true, false, "Marseille", new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Utc), 32000m, 26000m, "Technicien Support Client" },
                    { 66, 1, 14, "CDI", "Développez notre plateforme de réservation de voyages personnalisés. Moteur de recommandation IA, intégration APIs partenaires (hôtels, vols, activités), checkout sécurisé. Next.js 15, TypeScript, Prisma, Stripe.", null, true, true, "Nice", new DateTime(2026, 1, 31, 0, 0, 0, 0, DateTimeKind.Utc), 58000m, 44000m, "Développeur Full Stack Next.js" },
                    { 67, 3, 14, "CDI", "Améliorez notre moteur de recommandation d'itinéraires de voyage. Systèmes de recommandation collaboratifs et content-based, NLP pour l'analyse d'avis, personnalisation en temps réel. Python, TensorFlow, Redis.", null, true, true, "Nice", new DateTime(2026, 2, 14, 0, 0, 0, 0, DateTimeKind.Utc), 62000m, 48000m, "Data Scientist Recommandation" },
                    { 68, 6, 14, "CDI", "Pilotez la stratégie marketing de Voyageo sur 8 marchés européens. Campagnes multicanal, partenariats influenceurs voyage, affiliation et optimisation du funnel d'acquisition. Budget annuel de 2M€.", null, true, false, "Nice", new DateTime(2026, 2, 28, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 50000m, "Responsable Marketing Voyage" },
                    { 69, 2, 14, "Freelance", "Créez des interfaces inspirantes pour notre plateforme de voyage. Design de pages destination, configurateur d'itinéraire, cartes interactives. Sensibilité pour le travel, maîtrise de Figma et connaissance des design tokens.", null, true, true, "Nice", new DateTime(2026, 3, 13, 0, 0, 0, 0, DateTimeKind.Utc), 500m, 350m, "Designer UI Voyage" },
                    { 70, 1, 14, "Stage", "Stage de 6 mois pour développer des APIs et des intégrations avec nos partenaires (Amadeus, Booking, Viator). Node.js, TypeScript, tests automatisés. Environnement startup dynamique et international.", null, true, false, "Nice", new DateTime(2026, 3, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1200m, 900m, "Stage Développeur Backend" },
                    { 71, 5, 15, "CDI", "Intégrez la sécurité dans le cycle de développement de nos solutions bancaires (SAST, DAST, SCA). Revue de code sécurité, modélisation des menaces (STRIDE) et formation des développeurs aux bonnes pratiques OWASP.", null, true, true, "Paris", new DateTime(2026, 1, 22, 0, 0, 0, 0, DateTimeKind.Utc), 72000m, 55000m, "Ingénieur Sécurité Applicative" },
                    { 72, 1, 15, "CDI", "Développez nos solutions de monitoring et d'anti-fraude transactionnelle. Traitement en temps réel de millions de transactions, règles de détection et machine learning. Java 21, Spring, Kafka Streams, Drools.", null, true, true, "Paris", new DateTime(2026, 2, 5, 0, 0, 0, 0, DateTimeKind.Utc), 68000m, 50000m, "Développeur Java Sécurité Transactionnelle" },
                    { 73, 3, 15, "CDI", "Analysez les patterns de fraude dans les transactions bancaires. Développez des règles de détection, améliorez les modèles ML existants et collaborez avec les équipes compliance. SQL avancé, Python, Tableau.", null, true, false, "Paris", new DateTime(2026, 2, 20, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Analyste Fraude" },
                    { 74, 7, 15, "CDI", "Pilotez les projets d'automatisation du KYC (Know Your Customer) pour nos clients bancaires. Coordination technique et métier, suivi réglementaire et déploiement chez les clients. PMP et connaissance bancaire requises.", null, true, false, "Paris", new DateTime(2026, 3, 6, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 52000m, "Chef de Projet KYC / Conformité" },
                    { 75, 5, 15, "CDI", "Réalisez des tests d'intrusion sur les applications web et APIs de nos clients du secteur financier. Rédaction de rapports détaillés avec remédiation. Méthodologie OWASP, Burp Suite, scripts Python. OSCP requis.", null, true, false, "Paris", new DateTime(2026, 3, 17, 0, 0, 0, 0, DateTimeKind.Utc), 70000m, 50000m, "Pentester Web & API" },
                    { 76, 1, 16, "CDI", "Développez le firmware de nos capteurs agricoles connectés (humidité, température, pH). Communication LoRaWAN/NB-IoT, gestion d'énergie et OTA updates. C embarqué, STM32, protocoles LPWAN.", null, true, false, "Toulouse", new DateTime(2026, 1, 28, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Ingénieur IoT Embarqué" },
                    { 77, 3, 16, "CDI", "Développez des modèles prédictifs pour l'agriculture : prévision de rendement, détection de maladies par imagerie satellite et drone, optimisation de l'irrigation. Python, TensorFlow, géospatial (GDAL, GeoPandas).", null, true, true, "Toulouse", new DateTime(2026, 2, 12, 0, 0, 0, 0, DateTimeKind.Utc), 60000m, 45000m, "Data Scientist Agriculture de Précision" },
                    { 78, 1, 16, "CDI", "Construisez les APIs de notre plateforme de données agricoles. Ingestion de données IoT en temps réel, calcul de métriques et exposition aux dashboards. Python 3.12, FastAPI, TimescaleDB, InfluxDB.", null, true, true, "Toulouse", new DateTime(2026, 2, 27, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Développeur Backend Python / FastAPI" },
                    { 79, 6, 16, "CDI", "Développez le portefeuille clients d'AgriSmart auprès des coopératives et exploitations agricoles du Sud-Ouest. Démonstrations terrain, accompagnement technique et suivi post-vente. Déplacements fréquents.", null, true, false, "Toulouse", new DateTime(2026, 3, 11, 0, 0, 0, 0, DateTimeKind.Utc), 48000m, 35000m, "Technico-Commercial AgriTech" },
                    { 80, 3, 16, "Stage", "Stage de 6 mois pour travailler sur le traitement et la visualisation des données de nos capteurs agricoles. Pipeline de données, dashboards Grafana et analyses statistiques. Python, SQL, InfluxDB.", null, true, false, "Toulouse", new DateTime(2026, 3, 24, 0, 0, 0, 0, DateTimeKind.Utc), 1100m, 900m, "Stage Ingénieur Data IoT" },
                    { 81, 1, 17, "CDI", "Implémentez les mécaniques de gameplay de notre prochain jeu narratif AAA. Systèmes de combat, IA ennemie, physique et interactions environnementales. C++, Unreal Engine 5, Blueprints. Passion pour le jeu vidéo indispensable.", null, true, false, "Lyon", new DateTime(2026, 1, 25, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 40000m, "Programmeur Gameplay C++ / Unreal" },
                    { 82, 2, 17, "CDI", "Concevez des niveaux immersifs et stimulants pour notre jeu d'aventure. Blockout, scripting d'événements, placement d'ennemis et de récompenses, playtesting itératif. Maîtrise d'Unreal Editor et sens aigu du game feel.", null, true, false, "Lyon", new DateTime(2026, 2, 8, 0, 0, 0, 0, DateTimeKind.Utc), 45000m, 35000m, "Level Designer" },
                    { 83, 2, 17, "CDD", "Créez des environnements 3D photoréalistes pour notre jeu en monde ouvert. Modélisation, texturing (Substance Painter), intégration dans Unreal et optimisation pour les performances temps réel.", null, true, false, "Lyon", new DateTime(2026, 2, 22, 0, 0, 0, 0, DateTimeKind.Utc), 42000m, 33000m, "Artiste 3D Environnement" },
                    { 84, 2, 17, "CDI", "Créez l'univers sonore de nos jeux : effets, ambiances, musique adaptative et voiceover. Wwise, FMOD, enregistrement terrain et mixage interactif. Portfolio audio gaming requis.", null, true, false, "Lyon", new DateTime(2026, 3, 5, 0, 0, 0, 0, DateTimeKind.Utc), 40000m, 32000m, "Sound Designer Jeux Vidéo" },
                    { 85, 1, 17, "Stage", "Stage de 6 mois pour développer des outils internes facilitant le travail des level designers et artistes. Scripts Python, plugins Unreal, interfaces Qt. Passion pour le gamedev et les outils de production.", null, true, false, "Lyon", new DateTime(2026, 3, 19, 0, 0, 0, 0, DateTimeKind.Utc), 1100m, 900m, "Stage Programmeur Tools" },
                    { 86, 3, 18, "CDI", "Développez des modèles de traitement du langage naturel pour l'analyse automatique de contrats juridiques. Fine-tuning de LLMs, RAG, extraction d'entités et classification de clauses. Python, Hugging Face, LangChain.", null, true, true, "Bordeaux", new DateTime(2026, 1, 30, 0, 0, 0, 0, DateTimeKind.Utc), 68000m, 50000m, "Ingénieur NLP / LLM" },
                    { 87, 1, 18, "CDI", "Développez notre plateforme d'automatisation juridique. Interface d'analyse de contrats, workflows de validation et collaboration en temps réel. Next.js, tRPC, Prisma, PostgreSQL, WebSocket.", null, true, true, "Bordeaux", new DateTime(2026, 2, 13, 0, 0, 0, 0, DateTimeKind.Utc), 56000m, 42000m, "Développeur Full Stack TypeScript" },
                    { 88, 7, 18, "CDI", "Accompagnez nos clients (cabinets d'avocats et directions juridiques) dans la transformation digitale de leurs processus. Audit, paramétrage de la plateforme, formation et conduite du changement.", null, true, false, "Bordeaux", new DateTime(2026, 2, 27, 0, 0, 0, 0, DateTimeKind.Utc), 52000m, 40000m, "Legal Operations Manager" },
                    { 89, 4, 18, "CDI", "Mettez en place et maintenez l'infrastructure de notre SaaS juridique. Déploiements zero-downtime, gestion des environnements, monitoring et sécurité. Docker, Kubernetes, GitHub Actions, Datadog, AWS.", null, true, true, "Bordeaux", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), 60000m, 45000m, "DevOps Engineer" },
                    { 90, 1, 18, "Freelance", "Mission de 3 mois pour intégrer les nouvelles maquettes Figma de notre plateforme. HTML sémantique, CSS moderne (Grid, Container Queries), responsive et accessibilité. Autonomie et rigueur pixel-perfect.", null, true, true, "Bordeaux", new DateTime(2026, 3, 25, 0, 0, 0, 0, DateTimeKind.Utc), 550m, 400m, "Freelance Intégrateur Figma / CSS" },
                    { 91, 1, 19, "CDI", "Développez des jumeaux numériques urbains pour simuler les flux de mobilité et la consommation énergétique des bâtiments. Unity, CesiumJS, APIs géospatiales et modélisation 3D. Connaissance BIM appréciée.", null, true, false, "Nantes", new DateTime(2026, 2, 3, 0, 0, 0, 0, DateTimeKind.Utc), 60000m, 45000m, "Ingénieur Jumeaux Numériques" },
                    { 92, 3, 19, "CDI", "Construisez les pipelines de données alimentant nos modèles de mobilité intelligente. Données GTFS, données temps réel des capteurs de trafic et open data. Spark, Kafka, PostGIS, dbt.", null, true, true, "Nantes", new DateTime(2026, 2, 17, 0, 0, 0, 0, DateTimeKind.Utc), 56000m, 42000m, "Data Engineer Mobilité Urbaine" },
                    { 93, 7, 19, "CDI", "Coordonnez les projets d'innovation urbaine avec les collectivités locales. Ateliers de co-construction, gestion des consortiums de partenaires, suivi budgétaire et reporting aux financeurs (BPI, Europe).", null, true, false, "Nantes", new DateTime(2026, 3, 3, 0, 0, 0, 0, DateTimeKind.Utc), 58000m, 44000m, "Chef de Projet Smart City" },
                    { 94, 1, 19, "CDD", "Développez les composants géospatiaux de notre plateforme de visualisation urbaine. Traitements géographiques, APIs de cartographie et intégration de données multi-sources. Python, GeoDjango, PostGIS, Mapbox.", null, true, true, "Nantes", new DateTime(2026, 3, 16, 0, 0, 0, 0, DateTimeKind.Utc), 48000m, 38000m, "Développeur GIS Python" },
                    { 95, 3, 19, "Alternance", "Alternance de 12 mois pour analyser les données urbaines collectées par nos capteurs et nos partenaires open data. Création de dashboards, rapports automatisés et études statistiques.", null, true, false, "Nantes", new DateTime(2026, 3, 28, 0, 0, 0, 0, DateTimeKind.Utc), 1300m, 1000m, "Alternance Data Analyst Ville Connectée" },
                    { 96, 1, 20, "CDI", "Développez les smart contracts de traçabilité alimentaire sur notre blockchain privée. Solidity, Hardhat, Hyperledger Besu, intégration avec notre backend Node.js. Passion pour la transparence alimentaire.", null, true, true, "Montpellier", new DateTime(2026, 2, 6, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 48000m, "Développeur Blockchain Solidity" },
                    { 97, 1, 20, "CDI", "Construisez les APIs GraphQL de notre plateforme de traçabilité. Intégration avec les systèmes des producteurs et distributeurs, gestion des identités et audit trail complet. Node.js, Apollo, PostgreSQL.", null, true, true, "Montpellier", new DateTime(2026, 2, 20, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Développeur Backend Node.js / GraphQL" },
                    { 98, 7, 20, "CDI", "Définissez et mettez en œuvre la stratégie qualité de nos produits. Automatisation des tests, revues de code, métriques qualité et amélioration continue des processus de développement. Expérience en environnement blockchain appréciée.", null, true, false, "Montpellier", new DateTime(2026, 3, 6, 0, 0, 0, 0, DateTimeKind.Utc), 58000m, 45000m, "Responsable Qualité Logicielle" },
                    { 99, 2, 20, "Freelance", "Concevez l'interface de notre application de traçabilité alimentaire destinée aux consommateurs. Scan de produits, timeline de traçabilité, certifications et avis. Design mobile-first, Figma, prototypage.", null, true, true, "Montpellier", new DateTime(2026, 3, 18, 0, 0, 0, 0, DateTimeKind.Utc), 500m, 350m, "Designer UI/UX FoodTech" },
                    { 100, 6, 20, "CDI", "Développez le portefeuille commercial de FoodChain auprès des enseignes de grande distribution et des coopératives agricoles. Prospection, démos produit, négociation et suivi du cycle de vente.", null, true, false, "Montpellier", new DateTime(2026, 3, 27, 0, 0, 0, 0, DateTimeKind.Utc), 50000m, 35000m, "Business Developer FoodTech" },
                    { 101, 1, 7, "CDI", "Développez des APIs haute performance pour notre plateforme fintech. Architecture hexagonale, tests exhaustifs et documentation OpenAPI. Python 3.12, FastAPI, SQLAlchemy, Redis, Docker.", null, true, true, "Paris", new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Utc), 58000m, 45000m, "Développeur Python / FastAPI" },
                    { 102, 4, 15, "CDI", "Intégrez la sécurité dans nos pipelines CI/CD. Scan de vulnérabilités, SAST/DAST automatisé, gestion des secrets et conformité SOC 2. GitHub Actions, Snyk, Vault, Terraform, AWS.", null, true, true, "Paris", new DateTime(2026, 3, 25, 0, 0, 0, 0, DateTimeKind.Utc), 72000m, 55000m, "Ingénieur DevSecOps" },
                    { 103, 2, 10, "CDD", "Rédigez les contenus d'interface de notre application de santé. Microcopy, messages d'erreur, onboarding, emails transactionnels et documentation utilisateur. Ton empathique et clair adapté au contexte médical.", null, true, true, "Strasbourg", new DateTime(2026, 3, 28, 0, 0, 0, 0, DateTimeKind.Utc), 40000m, 32000m, "UX Writer" },
                    { 104, 4, 17, "CDI", "Assurez la haute disponibilité (99.99%) de notre infrastructure de jeu en ligne. Monitoring, incident management, capacity planning et automatisation. Go, Prometheus, Grafana, Terraform, GCP.", null, true, false, "Lyon", new DateTime(2026, 3, 30, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 50000m, "SRE / Ingénieur Fiabilité" },
                    { 105, 8, 11, "CDI", "Encadrez une équipe de 6 techniciens support (N1/N2/N3). Organisation des astreintes, amélioration des SLA, mise en place d'une base de connaissances et reporting hebdomadaire à la DSI. ITIL v4 requis.", null, true, false, "Lille", new DateTime(2026, 4, 1, 0, 0, 0, 0, DateTimeKind.Utc), 50000m, 40000m, "Responsable Support IT" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 31);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 43);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 68);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 71);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 72);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 73);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 74);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 75);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 76);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 77);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 78);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 79);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 80);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 81);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 82);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 83);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 84);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 85);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 86);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 87);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 88);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 89);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 90);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 91);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 92);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 93);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 94);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 95);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 96);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 97);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 98);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 99);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 100);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 101);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 102);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 103);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 104);

            migrationBuilder.DeleteData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 105);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "Description", "LogoUrl" },
                values: new object[] { new DateTime(2024, 1, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Leader français du développement logiciel, spécialisé dans les solutions cloud innovantes.", "https://ui-avatars.com/api/?name=TC&background=4f46e5&color=fff&size=128" });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "Description" },
                values: new object[] { new DateTime(2024, 2, 20, 0, 0, 0, 0, DateTimeKind.Utc), "Startup spécialisée en intelligence artificielle et analyse de données massives." });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "Description" },
                values: new object[] { new DateTime(2024, 3, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Expert en solutions d'infrastructure cloud et DevOps pour entreprises." });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "Description", "LogoUrl" },
                values: new object[] { new DateTime(2024, 4, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Agence de design UX/UI primée, créant des expériences numériques mémorables.", "https://ui-avatars.com/api/?name=DH&background=e11d48&color=fff&size=128" });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreatedAt", "Description" },
                values: new object[] { new DateTime(2024, 5, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cabinet de conseil en cybersécurité protégeant les entreprises du CAC 40." });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "PublishedAt" },
                values: new object[] { "Nous recherchons un développeur Full Stack passionné pour rejoindre notre équipe produit. Vous travaillerez sur notre plateforme SaaS utilisée par plus de 10 000 entreprises. Stack technique : Angular 19, .NET 8, SQL Server, Azure.", new DateTime(2026, 3, 15, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CategoryId", "CompanyId", "Description", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 3, 2, "Rejoignez notre équipe Data pour concevoir et déployer des modèles de machine learning en production. Vous travaillerez sur des problématiques de NLP et de computer vision avec des datasets à grande échelle.", "Lyon", new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Utc), 75000m, 55000m, "Data Scientist Senior" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CategoryId", "CompanyId", "Description", "IsRemote", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 4, 3, "Vous serez responsable de la fiabilité et de la scalabilité de notre infrastructure cloud. Kubernetes, Terraform, CI/CD, monitoring et automatisation seront votre quotidien.", false, "Bordeaux", new DateTime(2026, 3, 25, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 50000m, "Ingénieur DevOps / SRE" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CategoryId", "CompanyId", "ContractType", "Description", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 2, 4, "CDD", "Nous cherchons un designer créatif pour concevoir des interfaces utilisateur exceptionnelles. Vous mènerez des recherches utilisateurs, créerez des wireframes et prototypes, et collaborerez étroitement avec les développeurs.", "Nantes", new DateTime(2026, 3, 28, 0, 0, 0, 0, DateTimeKind.Utc), 48000m, 38000m, "UX/UI Designer" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CategoryId", "CompanyId", "Description", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { 5, 5, "Intégrez notre équipe de pentesters et auditeurs sécurité. Vous réaliserez des tests d'intrusion, des audits de code et accompagnerez nos clients dans leur stratégie de sécurité.", "Toulouse", new DateTime(2026, 4, 1, 0, 0, 0, 0, DateTimeKind.Utc), 68000m, 48000m, "Consultant Cybersécurité" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "ContractType", "Description", "IsRemote", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { "CDI", "Participez au développement de nos applications web modernes. Vous serez en charge de créer des composants réutilisables, d'optimiser les performances et d'assurer l'accessibilité.", true, new DateTime(2026, 4, 2, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Développeur Frontend React / Vue.js" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "ContractType", "Description", "IsRemote", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[] { "Stage", "Stage de 6 mois au sein de l'équipe Data. Vous participerez à l'analyse de données, la création de dashboards et l'automatisation de rapports avec Python et Power BI.", false, new DateTime(2026, 4, 3, 0, 0, 0, 0, DateTimeKind.Utc), 1200m, 800m, "Stage Data Analyst" });

            migrationBuilder.UpdateData(
                table: "JobOffers",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "CategoryId", "CompanyId", "Description", "Location", "PublishedAt", "SalaryMax", "Title" },
                values: new object[] { 7, 1, "Pilotez des projets de transformation digitale pour nos clients grands comptes. Méthodologie Agile, gestion budgétaire et coordination d'équipes pluridisciplinaires.", "Paris", new DateTime(2026, 4, 4, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, "Chef de Projet Digital" });
        }
    }
}
