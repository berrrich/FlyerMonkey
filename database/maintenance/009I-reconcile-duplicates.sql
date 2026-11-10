/* Mission 009I. Reviewable preparation only; never run against production without
   separate execution authorization and the recovery gates in docs/mission009i-reconciliation.md.
   Edit only the configuration block in a reviewed execution copy. Default rolls back.
   Requires a dedicated connection, VIEW DEFINITION, SELECT and appropriate DELETE rights.
   No blob operations. No extraction/history operations. No GO batches. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 15000;
DECLARE @ExpectedServer sysname = N'__CONFIRM_SERVER__';
DECLARE @ExpectedDatabase sysname = N'__CONFIRM_DATABASE__';
DECLARE @Commit bit = 0;
DECLARE @Authorization nvarchar(100) = N'';
DECLARE @DevForceFailure bit = 0;
DECLARE @AsOf date = '20261110'; -- audit comparison date, not the execution date
IF @@TRANCOUNT <> 0 THROW 50900, 'Use a dedicated connection without an ambient transaction.', 1;
IF CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) <> @ExpectedServer OR DB_NAME() <> @ExpectedDatabase
    THROW 50901, 'Explicit server/database confirmation failed.', 1;
IF @Commit = 1 AND @Authorization <> N'MISSION 009I EXECUTION AUTHORIZED'
    THROW 50902, 'Commit requires explicit execution authorization.', 1;
IF @DevForceFailure = 1 AND (CONVERT(int,SERVERPROPERTY('EngineEdition')) = 5 OR DB_NAME() NOT LIKE 'Mission009I_Disposable[_]%')
    THROW 50903, 'Failure injection is restricted to the disposable local DEV database.', 1;
IF HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION') <> 1
    THROW 50904, 'Full database metadata visibility required.', 1;

DECLARE @Expected TABLE (OfferID bigint PRIMARY KEY, ProductID int UNIQUE, KeepOfferID bigint,
    KeepProductID int, Name nvarchar(250), Brand nvarchar(100), Variant nvarchar(150), Pack nvarchar(100),
    Category nvarchar(100), Price decimal(10,2), Regular decimal(10,2), Saving decimal(10,2),
    Promo nvarchar(500), UnitText nvarchar(100), PromotionTypes nvarchar(100), ImagePath nvarchar(500));
INSERT @Expected VALUES
 (153,161,153,161,N'Australian Navel Oranges',N'',N'',N'kg',N'Produce',3.50,NULL,NULL,N'',N'',N'[]',N'product-images/2161fd43d83146a689b91516a2fcb07e.jpg'),
 (159,167,153,161,N'Australian Navel Oranges',N'',N'',N'kg',N'Produce',3.50,NULL,NULL,N'',N'',N'[]',N'product-images/2161fd43d83146a689b91516a2fcb07e.jpg'),
 (171,179,153,161,N'Australian Navel Oranges',N'',N'',N'kg',N'Produce',3.50,NULL,NULL,N'',N'',N'[]',N'product-images/2161fd43d83146a689b91516a2fcb07e.jpg'),
 (154,162,154,162,N'Chicken Garlic Kyly',N'',N'',N'ea',N'Deli',1.50,NULL,1.50,N'',N'',N'["Half price"]',N'product-images/52330bd6eec1437f89b308c6bf0b62c3.jpg'),
 (160,168,154,162,N'Chicken Garlic Kyly',N'',N'',N'ea',N'Deli',1.50,NULL,1.50,N'',N'',N'["Half price"]',N'product-images/52330bd6eec1437f89b308c6bf0b62c3.jpg'),
 (172,180,154,162,N'Chicken Garlic Kyly',N'',N'',N'ea',N'Deli',1.50,NULL,1.50,N'',N'',N'["Half price"]',N'product-images/52330bd6eec1437f89b308c6bf0b62c3.jpg'),
 (155,163,155,163,N'Muscle Nation High Protein Bar',N'Muscle Nation',N'Choc Brownie',N'70g',N'Snacks',2.75,3.50,2.75,N'1/2 Price',N'$3.93 per 100g',N'[]',N'product-images/59716d16c70941848c66f33ddf3db515.jpg'),
 (161,169,155,163,N'Muscle Nation High Protein Bar',N'Muscle Nation',N'Choc Brownie',N'70g',N'Snacks',2.75,3.50,2.75,N'1/2 Price',N'$3.93 per 100g',N'[]',N'product-images/59716d16c70941848c66f33ddf3db515.jpg'),
 (173,181,155,163,N'Muscle Nation High Protein Bar',N'Muscle Nation',N'Choc Brownie',N'70g',N'Snacks',2.75,3.50,2.75,N'1/2 Price',N'$3.93 per 100g',N'[]',N'product-images/59716d16c70941848c66f33ddf3db515.jpg'),
 (156,164,156,164,N'Zooper Dooper Ice Blocks',N'Zooper Dooper',N'Cosmic',N'24 x 70ml',N'Frozen',4.00,NULL,4.00,N'',N'$24.00 per 100ml',N'["Half price"]',N'product-images/3a14fa517773491aae6779b3e2c50a55.jpg'),
 (162,170,156,164,N'Zooper Dooper Ice Blocks',N'Zooper Dooper',N'Cosmic',N'24 x 70ml',N'Frozen',4.00,NULL,4.00,N'',N'$24.00 per 100ml',N'["Half price"]',N'product-images/3a14fa517773491aae6779b3e2c50a55.jpg'),
 (174,182,156,164,N'Zooper Dooper Ice Blocks',N'Zooper Dooper',N'Cosmic',N'24 x 70ml',N'Frozen',4.00,NULL,4.00,N'',N'$24.00 per 100ml',N'["Half price"]',N'product-images/3a14fa517773491aae6779b3e2c50a55.jpg'),
 (157,165,157,165,N'Vanish Gold Stain Remover Powder',N'Vanish',N'',N'2 kg',N'Laundry',17.00,NULL,19.00,N'Better than 1/2 Price',N'$8.50 per kg',N'[]',N'product-images/1fe482cab76d47dc8c9fe9053b29d5fa.jpg'),
 (163,171,157,165,N'Vanish Gold Stain Remover Powder',N'Vanish',N'',N'2 kg',N'Laundry',17.00,NULL,19.00,N'Better than 1/2 Price',N'$8.50 per kg',N'[]',N'product-images/1fe482cab76d47dc8c9fe9053b29d5fa.jpg'),
 (175,183,157,165,N'Vanish Gold Stain Remover Powder',N'Vanish',N'',N'2 kg',N'Laundry',17.00,NULL,19.00,N'Better than 1/2 Price',N'$8.50 per kg',N'[]',N'product-images/1fe482cab76d47dc8c9fe9053b29d5fa.jpg');
DECLARE @DeletedOffers TABLE(OfferID bigint,ProductID int);
DECLARE @DeletedProducts TABLE(ProductID int);
DECLARE @DeletedLinks TABLE(ProductID int,CategoryID int,IsPrimary bit);
BEGIN TRY
 BEGIN TRANSACTION;
 -- Lock every row/range in the small affected/reference tables until decision.
 -- This conservative audit intentionally blocks concurrent catalogue writers.
 SELECT * INTO #OffersBefore FROM dbo.Offers WITH(TABLOCKX,HOLDLOCK);
 SELECT * INTO #ProductsBefore FROM dbo.Products WITH(TABLOCKX,HOLDLOCK);
 SELECT * INTO #LinksBefore FROM dbo.ProductCategories WITH(TABLOCKX,HOLDLOCK);
 SELECT * INTO #CategoriesBefore FROM dbo.Categories WITH(TABLOCKX,HOLDLOCK);
 SELECT * INTO #LocationsBefore FROM dbo.RetailerLocations WITH(TABLOCKX,HOLDLOCK);
 SELECT * INTO #RetailersBefore FROM dbo.Retailers WITH(TABLOCKX,HOLDLOCK);
 SELECT * INTO #ListingsBefore FROM dbo.ProviderListings WITH(TABLOCKX,HOLDLOCK);
 SELECT * INTO #PromotionsBefore FROM dbo.OfferPromotionTypes WITH(TABLOCKX,HOLDLOCK);
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Products')) <> 10
 OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Offers')) <> 22
 OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.ProductCategories')) <> 3
    THROW 50905, 'Unexpected target-table columns; review schema drift.', 1;
 IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id IN(OBJECT_ID('dbo.Offers'),OBJECT_ID('dbo.Products'),OBJECT_ID('dbo.ProductCategories')))
    THROW 50906, 'Unexpected target trigger.', 1;
 -- Reject new, composite, disabled, untrusted or cascading inbound dependencies.
 IF EXISTS(SELECT 1 FROM sys.foreign_keys fk WHERE fk.referenced_object_id IN
       (OBJECT_ID('dbo.Products'),OBJECT_ID('dbo.Offers'),OBJECT_ID('dbo.ProductCategories'))
       AND (fk.is_disabled=1 OR fk.is_not_trusted=1 OR fk.delete_referential_action<>0
        OR (SELECT COUNT(*) FROM sys.foreign_key_columns f WHERE f.constraint_object_id=fk.object_id)<>1
        OR NOT EXISTS(SELECT 1 FROM sys.foreign_key_columns f WHERE f.constraint_object_id=fk.object_id AND
          ((f.parent_object_id=OBJECT_ID('dbo.Offers') AND COL_NAME(f.parent_object_id,f.parent_column_id)='ProductID' AND f.referenced_object_id=OBJECT_ID('dbo.Products') AND COL_NAME(f.referenced_object_id,f.referenced_column_id)='ID')
          OR (f.parent_object_id=OBJECT_ID('dbo.ProductCategories') AND COL_NAME(f.parent_object_id,f.parent_column_id)='ProductID' AND f.referenced_object_id=OBJECT_ID('dbo.Products') AND COL_NAME(f.referenced_object_id,f.referenced_column_id)='ID')
          OR (f.parent_object_id=OBJECT_ID('dbo.ProviderListings') AND COL_NAME(f.parent_object_id,f.parent_column_id)='ProductID' AND f.referenced_object_id=OBJECT_ID('dbo.Products') AND COL_NAME(f.referenced_object_id,f.referenced_column_id)='ID')
          OR (f.parent_object_id=OBJECT_ID('dbo.OfferPromotionTypes') AND COL_NAME(f.parent_object_id,f.parent_column_id)='OfferID' AND f.referenced_object_id=OBJECT_ID('dbo.Offers') AND COL_NAME(f.referenced_object_id,f.referenced_column_id)='ID')))))
    THROW 50907, 'Unexpected inbound foreign-key dependency.', 1;
 IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id IN(OBJECT_ID('dbo.Products'),OBJECT_ID('dbo.Offers'),OBJECT_ID('dbo.ProductCategories')))<>4
    THROW 50908, 'Expected inbound foreign keys missing or changed.', 1;
 IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE (parent_object_id IN(OBJECT_ID('dbo.Offers'),OBJECT_ID('dbo.Products'),OBJECT_ID('dbo.ProductCategories'))) AND (is_disabled=1 OR is_not_trusted=1))
    THROW 50909, 'Disabled or untrusted target constraints.', 1;
 IF NOT EXISTS(SELECT 1 FROM dbo.Retailers WHERE ID=1 AND Name=N'Woolworths')
 OR NOT EXISTS(SELECT 1 FROM dbo.RetailerLocations WHERE ID=3 AND RetailerID=1 AND StoreName=N'Woolworths Butler' AND Suburb=N'Butler')
 OR NOT EXISTS(SELECT 1 FROM dbo.Categories WHERE ID=4 AND Name=N'Deli' AND IsActive=1)
 OR NOT EXISTS(SELECT 1 FROM dbo.Categories WHERE ID=2 AND Name=N'Meat & Poultry' AND IsActive=1)
    THROW 50910, 'Reference identities changed.', 1;
 DECLARE @ExcessOffers int=(SELECT COUNT(*) FROM dbo.Offers o JOIN @Expected e ON o.ID=e.OfferID WHERE e.OfferID<>e.KeepOfferID);
 DECLARE @ExcessProducts int=(SELECT COUNT(*) FROM dbo.Products p JOIN @Expected e ON p.ID=e.ProductID WHERE e.ProductID<>e.KeepProductID);
 DECLARE @AlreadyClean bit=CASE WHEN @ExcessOffers=0 AND @ExcessProducts=0 THEN 1 ELSE 0 END;
 IF @AlreadyClean=0 AND (@ExcessOffers<>10 OR @ExcessProducts<>10)
    THROW 50911, 'Partial/mixed reconciliation state; manual review required.', 1;
 IF (SELECT COUNT(*) FROM dbo.Offers)<>109-10*@AlreadyClean
 OR (SELECT COUNT(*) FROM dbo.Products)<>112-10*@AlreadyClean
 OR (SELECT COUNT(*) FROM dbo.ProductCategories)<>57-4*@AlreadyClean
 OR (SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<=@AsOf AND ValidTo>=@AsOf)<>48-10*@AlreadyClean
 OR (SELECT COUNT(*) FROM dbo.Categories)<>19 OR (SELECT COUNT(*) FROM dbo.RetailerLocations)<>2
 OR (SELECT COUNT(DISTINCT ImageBlobPath) FROM dbo.Products)<>80
 OR EXISTS(SELECT 1 FROM dbo.ProviderListings) OR EXISTS(SELECT 1 FROM dbo.OfferPromotionTypes)
    THROW 50912, 'Audit baseline counts changed; fresh review required.', 1;
 IF (SELECT COUNT(*) FROM dbo.Offers o JOIN @Expected e ON e.OfferID=o.ID AND e.ProductID=o.ProductID)<>15-10*@AlreadyClean
 OR (SELECT COUNT(*) FROM dbo.Products p JOIN @Expected e ON e.ProductID=p.ID)<>15-10*@AlreadyClean
    THROW 50913, 'Explicit OfferID/ProductID pairing changed.', 1;
 -- JSON preserves nulls and string lengths/case, avoiding collation/null-equality shortcuts.
 IF EXISTS(SELECT 1 FROM @Expected e JOIN dbo.Products p ON p.ID=e.ProductID JOIN dbo.Offers o ON o.ID=e.OfferID
 CROSS APPLY(SELECT p.Name,p.Brand,p.Variant,p.PackSizeText,p.Barcode,p.Category,p.ImageBlobPath FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES) a(j)
 CROSS APPLY(SELECT e.Name,e.Brand,e.Variant,e.Pack AS PackSizeText,N'' AS Barcode,e.Category,e.ImagePath AS ImageBlobPath FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES) b(j)
 CROSS APPLY(SELECT o.AdvertisedPrice,o.RegularPrice,o.AdvertisedSaving,o.PromoText,o.UnitPriceText,o.PromotionTypes FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES) c(j)
 CROSS APPLY(SELECT e.Price AS AdvertisedPrice,e.Regular AS RegularPrice,e.Saving AS AdvertisedSaving,e.Promo AS PromoText,e.UnitText AS UnitPriceText,e.PromotionTypes FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES) d(j)
 WHERE CONVERT(varbinary(max),a.j)<>CONVERT(varbinary(max),b.j) OR CONVERT(varbinary(max),c.j)<>CONVERT(varbinary(max),d.j)
 OR o.ProductID<>e.ProductID OR o.RetailerID<>1 OR ISNULL(o.RetailerLocationID,-1)<>3
 OR o.ValidFrom<>'20261104' OR o.ValidTo<>'20261111'
 OR ISNULL(o.SourceType,N'')<>N'Flyer' OR ISNULL(o.SourceDescription,N'')<>N'page-002.pdf' OR ISNULL(o.FlyerPageNumber,-1)<>2
 OR o.FlyerImageBlobPath IS NOT NULL OR p.UpdatedUtc IS NOT NULL OR o.SourceProductText IS NOT NULL
 OR o.MultiBuyQuantity IS NOT NULL OR o.MultiBuyPrice IS NOT NULL OR o.MemberPrice IS NOT NULL OR o.MemberOnlyFlag IS NOT NULL)
    THROW 50914, 'Expected product/offer content changed.', 1;
 IF EXISTS(SELECT 1 FROM dbo.Offers o JOIN @Expected e ON e.ProductID=o.ProductID WHERE o.ID<>e.OfferID)
 OR EXISTS(SELECT 1 FROM dbo.ProviderListings l JOIN @Expected e ON e.ProductID=l.ProductID)
 OR EXISTS(SELECT 1 FROM dbo.OfferPromotionTypes t JOIN @Expected e ON e.OfferID=t.OfferID)
    THROW 50915, 'Additional offer/listing/promotion references found.', 1;
 IF EXISTS(SELECT 1 FROM @Expected e JOIN dbo.Products p ON p.ID=e.ProductID JOIN dbo.Products k ON k.ID=e.KeepProductID
 JOIN dbo.Offers o ON o.ID=e.OfferID JOIN dbo.Offers ko ON ko.ID=e.KeepOfferID
 WHERE p.CreatedUtc<k.CreatedUtc OR o.CreatedUtc<ko.CreatedUtc)
    THROW 50916, 'Retained pair is no longer the earliest equivalent.', 1;
 IF EXISTS(SELECT pc.ProductID,pc.CategoryID,pc.IsPrimary FROM dbo.ProductCategories pc JOIN @Expected e ON e.ProductID=pc.ProductID
 EXCEPT SELECT e.ProductID,c.CategoryID,c.IsPrimary FROM @Expected e JOIN dbo.Products p ON p.ID=e.ProductID CROSS JOIN(VALUES(2,CONVERT(bit,0)),(4,CONVERT(bit,1))) c(CategoryID,IsPrimary) WHERE e.KeepProductID=162)
 OR EXISTS(SELECT e.ProductID,c.CategoryID,c.IsPrimary FROM @Expected e JOIN dbo.Products p ON p.ID=e.ProductID CROSS JOIN(VALUES(2,CONVERT(bit,0)),(4,CONVERT(bit,1))) c(CategoryID,IsPrimary) WHERE e.KeepProductID=162
 EXCEPT SELECT pc.ProductID,pc.CategoryID,pc.IsPrimary FROM dbo.ProductCategories pc JOIN @Expected e ON e.ProductID=pc.ProductID)
    THROW 50917, 'Candidate category mapping changed.', 1;
 SELECT N'BEFORE' Phase,COUNT(*) Offers,(SELECT COUNT(*) FROM dbo.Products) Products,(SELECT COUNT(*) FROM dbo.ProductCategories) ProductCategoryLinks,
 (SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<=@AsOf AND ValidTo>=@AsOf) CurrentAtAuditDate,@AsOf AuditDate FROM dbo.Offers;
 SELECT OfferID,ProductID,KeepOfferID,KeepProductID,CASE WHEN OfferID=KeepOfferID THEN 'RETAIN' ELSE 'EXCESS' END Disposition FROM @Expected ORDER BY KeepOfferID,OfferID;
 IF @AlreadyClean=1
 BEGIN
    ROLLBACK;
    SELECT N'ALREADY_CLEAN_VALIDATED_NO_CHANGES' Outcome;
    RETURN;
 END;
 DELETE pc OUTPUT deleted.ProductID,deleted.CategoryID,deleted.IsPrimary INTO @DeletedLinks
 FROM dbo.ProductCategories pc JOIN @Expected e ON e.ProductID=pc.ProductID WHERE e.ProductID<>e.KeepProductID;
 IF @@ROWCOUNT<>4 THROW 50918, 'Expected exactly four excess category links.', 1;
 DELETE o OUTPUT deleted.ID,deleted.ProductID INTO @DeletedOffers
 FROM dbo.Offers o JOIN @Expected e ON o.ID=e.OfferID AND o.ProductID=e.ProductID WHERE e.OfferID<>e.KeepOfferID;
 IF @@ROWCOUNT<>10 THROW 50919, 'Expected exactly ten excess offers.', 1;
 DELETE p OUTPUT deleted.ID INTO @DeletedProducts
 FROM dbo.Products p JOIN @Expected e ON p.ID=e.ProductID WHERE e.ProductID<>e.KeepProductID;
 IF @@ROWCOUNT<>10 THROW 50920, 'Expected exactly ten excess products.', 1;
 IF @DevForceFailure=1 THROW 50921, 'Deliberate DEV assertion failure after all deletes.', 1;
 IF (SELECT COUNT(*) FROM dbo.Offers)<>99 OR (SELECT COUNT(*) FROM dbo.Products)<>102
 OR (SELECT COUNT(*) FROM dbo.ProductCategories)<>53
 OR (SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<=@AsOf AND ValidTo>=@AsOf)<>38
 OR (SELECT COUNT(DISTINCT ImageBlobPath) FROM dbo.Products)<>80
    THROW 50922, 'Post-change counts failed.', 1;
 -- Full surviving-row equality protects retained and unrelated rows, not counts alone.
 IF EXISTS(SELECT * FROM dbo.Offers EXCEPT SELECT b.* FROM #OffersBefore b WHERE NOT EXISTS(SELECT 1 FROM @DeletedOffers d WHERE d.OfferID=b.ID))
 OR EXISTS(SELECT b.* FROM #OffersBefore b WHERE NOT EXISTS(SELECT 1 FROM @DeletedOffers d WHERE d.OfferID=b.ID) EXCEPT SELECT * FROM dbo.Offers)
 OR EXISTS(SELECT * FROM dbo.Products EXCEPT SELECT b.* FROM #ProductsBefore b WHERE NOT EXISTS(SELECT 1 FROM @DeletedProducts d WHERE d.ProductID=b.ID))
 OR EXISTS(SELECT b.* FROM #ProductsBefore b WHERE NOT EXISTS(SELECT 1 FROM @DeletedProducts d WHERE d.ProductID=b.ID) EXCEPT SELECT * FROM dbo.Products)
 OR EXISTS(SELECT * FROM dbo.ProductCategories EXCEPT SELECT b.* FROM #LinksBefore b WHERE NOT EXISTS(SELECT 1 FROM @DeletedProducts d WHERE d.ProductID=b.ProductID))
 OR EXISTS(SELECT b.* FROM #LinksBefore b WHERE NOT EXISTS(SELECT 1 FROM @DeletedProducts d WHERE d.ProductID=b.ProductID) EXCEPT SELECT * FROM dbo.ProductCategories)
    THROW 50923, 'Unexpected surviving-row changes.', 1;
 IF EXISTS(SELECT * FROM dbo.Categories EXCEPT SELECT * FROM #CategoriesBefore) OR EXISTS(SELECT * FROM #CategoriesBefore EXCEPT SELECT * FROM dbo.Categories)
 OR EXISTS(SELECT * FROM dbo.RetailerLocations EXCEPT SELECT * FROM #LocationsBefore) OR EXISTS(SELECT * FROM #LocationsBefore EXCEPT SELECT * FROM dbo.RetailerLocations)
 OR EXISTS(SELECT * FROM dbo.Retailers EXCEPT SELECT * FROM #RetailersBefore) OR EXISTS(SELECT * FROM #RetailersBefore EXCEPT SELECT * FROM dbo.Retailers)
 OR EXISTS(SELECT * FROM dbo.ProviderListings EXCEPT SELECT * FROM #ListingsBefore) OR EXISTS(SELECT * FROM #ListingsBefore EXCEPT SELECT * FROM dbo.ProviderListings)
 OR EXISTS(SELECT * FROM dbo.OfferPromotionTypes EXCEPT SELECT * FROM #PromotionsBefore) OR EXISTS(SELECT * FROM #PromotionsBefore EXCEPT SELECT * FROM dbo.OfferPromotionTypes)
    THROW 50924, 'Protected relationships changed.', 1;
 SELECT N'AFTER_INSIDE_TRANSACTION' Phase,COUNT(*) Offers,(SELECT COUNT(*) FROM dbo.Products) Products,(SELECT COUNT(*) FROM dbo.ProductCategories) ProductCategoryLinks,
 (SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<=@AsOf AND ValidTo>=@AsOf) CurrentAtAuditDate,@AsOf AuditDate FROM dbo.Offers;
 SELECT * FROM @DeletedOffers ORDER BY OfferID;
 SELECT * FROM @DeletedProducts ORDER BY ProductID;
 SELECT * FROM @DeletedLinks ORDER BY ProductID,CategoryID;
 DROP TABLE #OffersBefore,#ProductsBefore,#LinksBefore,#CategoriesBefore,#LocationsBefore,#RetailersBefore,#ListingsBefore,#PromotionsBefore;
 IF @Commit=1 BEGIN COMMIT; SELECT N'COMMITTED' Outcome; END
 ELSE BEGIN ROLLBACK; SELECT N'DRY_RUN_ROLLED_BACK' Outcome; END;
 SELECT N'PERSISTED_AFTER_DECISION' Phase,COUNT(*) Offers,(SELECT COUNT(*) FROM dbo.Products) Products,(SELECT COUNT(*) FROM dbo.ProductCategories) ProductCategoryLinks,
 (SELECT COUNT(*) FROM dbo.Offers WHERE ValidFrom<=@AsOf AND ValidTo>=@AsOf) CurrentAtAuditDate,@AsOf AuditDate FROM dbo.Offers;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
